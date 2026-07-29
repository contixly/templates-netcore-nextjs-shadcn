using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;
using Template.Application.Accounts;
using Template.Application.Authentication;
using Template.Infrastructure.Health;
using Template.Infrastructure.Persistence;

namespace Template.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            builder.Configuration.AddJsonFile(
                "appsettings.Local.json",
                optional: true,
                reloadOnChange: true);
        }

        builder.Services.AddValidation();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthInfrastructure(
            builder.Configuration,
            builder.Environment);
        builder.Services.AddScoped<LocalAutomationAuthService>();
        builder.Services.AddScoped<BrowserAuthenticationService>();
        builder.Services.AddScoped<ExternalIdentityService>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<AccountSessionService>();
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

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<InvalidBrowserSessionCookieMiddleware>();
        app.UseAuthorization();
        app.MapEndpointModules();

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
        {
            app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
        }

        return app;
    }
}
