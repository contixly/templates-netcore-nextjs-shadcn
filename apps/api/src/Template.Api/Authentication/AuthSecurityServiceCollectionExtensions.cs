using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Template.Api.Errors;

namespace Template.Api.Authentication;

internal static class AuthRateLimitPolicies
{
    internal const string LocalAutomationCreate = "LocalAutomationCreate";
    internal const string LocalAutomationSignIn = "LocalAutomationSignIn";
    internal const string ExternalOAuthChallenge = "ExternalOAuthChallenge";
    internal const string ExternalOAuthCallback = "ExternalOAuthCallback";
}

internal sealed class ExternalOAuthSecurityOptions
{
    internal const string SectionName = "ExternalOAuthSecurity";

    public int ChallengePermitLimitPerMinute { get; set; } = 20;

    public int CallbackPermitLimitPerFiveMinutes { get; set; } = 60;

    public int CallbackConcurrencyLimit { get; set; } = 10;
}

internal static class AuthSecurityServiceCollectionExtensions
{
    internal static IServiceCollection AddApiAuthSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
        });
        services
            .AddOptions<LocalAutomationAuthOptions>()
            .Bind(configuration.GetSection(LocalAutomationAuthOptions.SectionName))
            .Validate(
                options =>
                    options.CreateRateLimitPerMinute > 0 &&
                    options.SignInRateLimitPerFiveMinutes > 0,
                "Local automation rate limits must be positive.")
            .ValidateOnStart();
        services
            .AddOptions<ExternalOAuthSecurityOptions>()
            .Bind(configuration.GetSection(
                ExternalOAuthSecurityOptions.SectionName))
            .Validate(
                options =>
                    options.ChallengePermitLimitPerMinute > 0
                    && options.CallbackPermitLimitPerFiveMinutes > 0
                    && options.CallbackConcurrencyLimit > 0,
                "External OAuth rate limits must be positive.")
            .ValidateOnStart();
        services.AddSingleton<
            ILocalAutomationAuthAvailability,
            LocalAutomationAuthAvailability>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Host-template.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.Cookie.Domain = null;
        });

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(
                AuthRateLimitPolicies.LocalAutomationCreate,
                context =>
                {
                    var local = context.RequestServices
                        .GetRequiredService<IOptions<LocalAutomationAuthOptions>>()
                        .Value;
                    return Partition(
                        context,
                        local.CreateRateLimitPerMinute,
                        TimeSpan.FromMinutes(1));
                });
            options.AddPolicy(
                AuthRateLimitPolicies.LocalAutomationSignIn,
                context =>
                {
                    var local = context.RequestServices
                        .GetRequiredService<IOptions<LocalAutomationAuthOptions>>()
                        .Value;
                    return Partition(
                        context,
                        local.SignInRateLimitPerFiveMinutes,
                        TimeSpan.FromMinutes(5));
                });
            options.AddPolicy(
                AuthRateLimitPolicies.ExternalOAuthChallenge,
                context =>
                {
                    var external = context.RequestServices
                        .GetRequiredService<
                            IOptions<ExternalOAuthSecurityOptions>>()
                        .Value;
                    return Partition(
                        context,
                        external.ChallengePermitLimitPerMinute,
                        TimeSpan.FromMinutes(1));
                });
            options.AddPolicy(
                AuthRateLimitPolicies.ExternalOAuthCallback,
                context =>
                {
                    var external = context.RequestServices
                        .GetRequiredService<
                            IOptions<ExternalOAuthSecurityOptions>>()
                        .Value;
                    var key =
                        context.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown";
                    return RateLimitPartition.Get(
                        key,
                        _ => RateLimiter.CreateChained(
                            new FixedWindowRateLimiter(
                                new FixedWindowRateLimiterOptions
                                {
                                    AutoReplenishment = true,
                                    PermitLimit =
                                        external
                                            .CallbackPermitLimitPerFiveMinutes,
                                    QueueLimit = 0,
                                    Window = TimeSpan.FromMinutes(5)
                                }),
                            new ConcurrencyLimiter(
                                new ConcurrencyLimiterOptions
                                {
                                    PermitLimit =
                                        external.CallbackConcurrencyLimit,
                                    QueueLimit = 0
                                })));
                });
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                if (rejected.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                {
                    rejected.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds)
                            .ToString(CultureInfo.InvariantCulture);
                }

                rejected.HttpContext.Response.StatusCode =
                    StatusCodes.Status429TooManyRequests;
                var details = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests
                };
                details.Extensions["code"] = ApiProblemCodes.RateLimited;
                await rejected.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>()
                    .TryWriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = rejected.HttpContext,
                        ProblemDetails = details
                    });
            };
        });
        return services;
    }

    private static RateLimitPartition<string> Partition(
        HttpContext context,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = window
            });
}
