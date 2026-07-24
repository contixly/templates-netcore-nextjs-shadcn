using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Infrastructure.Authentication;

namespace Template.Api.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    internal static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton<PostgresTicketStore>();
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultChallengeScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultForbidScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultSignOutScheme = ApiAuthenticationDefaults.SchemeName;
            })
            .AddCookie(ApiAuthenticationDefaults.SchemeName, options =>
            {
                ConfigureHostCookie(options);
                options.CookieManager = new PrimaryBrowserSessionCookieManager();
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddCookie(ApiAuthenticationDefaults.IssuerSchemeName, options =>
            {
                ConfigureHostCookie(options);
                options.CookieManager = new WriteOnlyBrowserSessionCookieManager();
            });

        ConfigurePersistentTicketServices(
            services,
            ApiAuthenticationDefaults.SchemeName);
        ConfigurePersistentTicketServices(
            services,
            ApiAuthenticationDefaults.IssuerSchemeName);

        services.AddAuthorization(options =>
            options.AddPolicy(
                ApiPolicies.Authenticated,
                policy => policy.RequireAuthenticatedUser()));

        return services;
    }

    private static void ConfigureHostCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = ApiAuthenticationDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.Cookie.Domain = null;
    }

    private static void ConfigurePersistentTicketServices(
        IServiceCollection services,
        string scheme)
    {
        services
            .AddOptions<CookieAuthenticationOptions>(scheme)
            .Configure<IDataProtectionProvider, PostgresTicketStore>(
                (options, dataProtectionProvider, store) =>
                {
                    options.SessionStore = store;
                    options.TicketDataFormat = new TicketDataFormat(
                        dataProtectionProvider.CreateProtector(
                            BrowserSessionAuthenticationDefaults
                                .TicketDataProtectionPurpose));
                });
    }
}
