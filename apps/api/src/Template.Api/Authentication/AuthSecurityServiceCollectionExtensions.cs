using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Template.Api.Errors;
using Template.Api.Features.Collaboration;
using Template.Application.Authentication.Ports;
using Template.Infrastructure.Authentication;

namespace Template.Api.Authentication;

internal static class AuthRateLimitPolicies
{
    internal static readonly object ExternalOAuthCallbackPreAuthenticationLease =
        new();
    internal const string LocalAutomationCreate = "LocalAutomationCreate";
    internal const string LocalAutomationSignIn = "LocalAutomationSignIn";
    internal const string ExternalOAuthChallenge = "ExternalOAuthChallenge";
    internal const string ExternalOAuthCallback = "ExternalOAuthCallback";
    internal const string InvitationCreate = "InvitationCreate";
    internal const string InvitationDecision = "InvitationDecision";
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
        var configuredPublicOrigin = configuration[
            $"{ExternalAuthenticationOptions.SectionName}:" +
            nameof(ExternalAuthenticationOptions.PublicOrigin)];
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            if (Uri.TryCreate(
                    configuredPublicOrigin,
                    UriKind.Absolute,
                    out var publicOrigin))
            {
                options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;
                options.AllowedHosts.Add(publicOrigin.Host);
            }
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
                    if (context.Items.ContainsKey(
                            AuthRateLimitPolicies
                                .ExternalOAuthCallbackPreAuthenticationLease))
                    {
                        return RateLimitPartition.GetNoLimiter(
                            "pre-authentication-callback-lease");
                    }

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
            options.AddPolicy(
                AuthRateLimitPolicies.InvitationCreate,
                context => UserPartition(
                    context,
                    permitLimit: 20,
                    TimeSpan.FromMinutes(1)));
            options.AddPolicy(
                AuthRateLimitPolicies.InvitationDecision,
                context => UserPartition(
                    context,
                    permitLimit: 30,
                    TimeSpan.FromMinutes(1)));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejected, cancellationToken) =>
            {
                rejected.HttpContext.Response.Headers.CacheControl = "no-store";
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
                await AuditInvitationRateLimitAsync(
                    rejected.HttpContext,
                    cancellationToken);
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

    private static RateLimitPartition<string> UserPartition(
        HttpContext context,
        int permitLimit,
        TimeSpan window)
    {
        var claims = context.User.FindAll(ClaimTypes.NameIdentifier).ToArray();
        var key = claims.Length == 1 &&
            Guid.TryParseExact(claims[0].Value, "D", out var userId) &&
            userId != Guid.Empty &&
            string.Equals(
                claims[0].Value,
                userId.ToString("D"),
                StringComparison.Ordinal)
                ? $"user:{userId:D}"
                : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = window
            });
    }

    private static async Task AuditInvitationRateLimitAsync(
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var metadata = http.GetEndpoint()?.Metadata
            .GetMetadata<InvitationRateLimitAuditMetadata>();
        if (metadata is null)
        {
            return;
        }

        try
        {
            var actor = await CollaborationEndpointBoundary.RequiredActorAsync(
                http.RequestServices
                    .GetRequiredService<IBrowserSessionGateway>(),
                http.User,
                cancellationToken);
            var organizationId = metadata.OrganizationRouteValueName is null
                ? null
                : CollaborationEndpointBoundary.CanonicalOpaqueId(
                    http.Request.RouteValues[
                        metadata.OrganizationRouteValueName]?.ToString());
            var invitationId = metadata.InvitationRouteValueName is null
                ? null
                : CollaborationEndpointBoundary.CanonicalOpaqueId(
                    http.Request.RouteValues[
                        metadata.InvitationRouteValueName]?.ToString());
            CollaborationEndpointBoundary.WriteInvitation(
                http.RequestServices
                    .GetRequiredService<ILogger<InvitationEndpointModule>>(),
                metadata.Operation,
                ApiProblemCodes.RateLimited,
                actor,
                organizationId,
                teamId: null,
                invitationId);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A limiter rejection remains safe when a stale session cannot be
            // resolved for its optional collaboration audit.
        }
    }
}
