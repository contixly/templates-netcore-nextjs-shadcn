using Microsoft.AspNetCore.Authentication.Cookies;

namespace Template.Api.Authentication;

internal static class AuthenticationServiceCollectionExtensions
{
    internal static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultChallengeScheme = ApiAuthenticationDefaults.SchemeName;
                options.DefaultForbidScheme = ApiAuthenticationDefaults.SchemeName;
            })
            .AddCookie(ApiAuthenticationDefaults.SchemeName, options =>
            {
                options.Cookie.Name = ApiAuthenticationDefaults.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.Domain = null;
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
            });

        services.AddAuthorization(options =>
            options.AddPolicy(
                ApiPolicies.Authenticated,
                policy => policy.RequireAuthenticatedUser()));

        return services;
    }
}
