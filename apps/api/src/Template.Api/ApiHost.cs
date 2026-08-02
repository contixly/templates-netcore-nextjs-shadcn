using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;
using Template.Application.Accounts;
using Template.Application.ApiKeys;
using Template.Application.Authentication;
using Template.Application.Collaboration;
using Template.Application.Organizations;
using Template.Infrastructure.Health;
using Template.Infrastructure.Persistence;

namespace Template.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args)
    {
        var isBuildTimeOpenApiExport = IsBuildTimeOpenApiExport();
        var builder = isBuildTimeOpenApiExport
            ? WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ApplicationName = typeof(ApiHost).Assembly.GetName().Name,
                EnvironmentName = "Test"
            })
            : WebApplication.CreateBuilder(args);
        if (isBuildTimeOpenApiExport)
        {
            builder.Logging.AddFilter(
                "Microsoft.AspNetCore.DataProtection",
                LogLevel.Error);
        }

        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile(
                "appsettings.Local.json",
                optional: true,
                reloadOnChange: true);
        }

        builder.Services.AddValidation();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.AllowDuplicateProperties = false);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthInfrastructure(
            builder.Configuration,
            builder.Environment);
        builder.Services.AddScoped<LocalAutomationAuthService>();
        builder.Services.AddScoped<BrowserAuthenticationService>();
        builder.Services.AddScoped<ExternalIdentityService>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<AccountSessionService>();
        builder.Services.AddScoped<ApiKeyManagementService>();
        builder.Services.AddScoped<ApiKeyAuthenticationService>();
        builder.Services.AddScoped<MachineApiService>();
        builder.Services.AddScoped<OrganizationService>();
        builder.Services.AddScoped<OrganizationMembershipService>();
        builder.Services.AddScoped<TeamService>();
        builder.Services.AddScoped<InvitationService>();
        builder.Services
            .AddHealthChecks()
            .AddCheck<AuthDatabaseHealthCheck>(
                "postgres-auth-schema",
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(2));
        builder.Services.AddApiAuthentication();
        builder.Services.AddApiAuthSecurity(builder.Configuration);
        builder.Services.AddApiErrorHandling();
        builder.Services.AddApiOpenApi();
        builder.Services.AddEndpointModules();
        if (isBuildTimeOpenApiExport)
        {
            builder.Services.PostConfigure<KeyManagementOptions>(options =>
                options.XmlRepository = BuildTimeOpenApiXmlRepository.Instance);
        }

        var app = builder.Build();

        if (app.Environment.IsEnvironment("Test") &&
            app.Configuration.GetValue<bool>("Testing:AssumeHttpsBoundary"))
        {
            app.Use((context, next) =>
            {
                context.Request.Scheme = Uri.UriSchemeHttps;
                return next(context);
            });
        }

        app.UseForwardedHeaders();
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/api"),
            api =>
            {
                api.UseMiddleware<CorrelationIdMiddleware>();
                api.UseMiddleware<RequestLoggingMiddleware>();
                api.UseExceptionHandler();
                api.UseStatusCodePages();
                api.UseMiddleware<AuthResponseCacheMiddleware>();
                api.UseMiddleware<LocalAutomationAvailabilityMiddleware>();
            });

        app.UseWhen(
            IsExternalOAuthCallback,
            callback =>
            {
                callback.UseRateLimiter();
                callback.Use((context, next) =>
                {
                    context.Items[
                        AuthRateLimitPolicies
                            .ExternalOAuthCallbackPreAuthenticationLease] = true;
                    return next(context);
                });
            });
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseMiddleware<InvalidBrowserSessionCookieMiddleware>();
        app.UseAuthorization();
        app.MapEndpointModules();

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
        {
            app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
        }

        return app;
    }

    private static bool IsBuildTimeOpenApiExport() =>
        string.Equals(
            Assembly.GetEntryAssembly()?.GetName().Name,
            "GetDocument.Insider",
            StringComparison.Ordinal);

    private static bool IsExternalOAuthCallback(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/auth/callback") ||
        context.Request.Path.StartsWithSegments(
            "/api/auth/oauth2/callback/yandex");

    private sealed class BuildTimeOpenApiXmlRepository : IXmlRepository
    {
        internal static BuildTimeOpenApiXmlRepository Instance { get; } = new();

        public IReadOnlyCollection<XElement> GetAllElements() => [];

        public void StoreElement(XElement _, string __)
        {
            // Build-time document generation never persists runtime key material.
        }
    }
}
