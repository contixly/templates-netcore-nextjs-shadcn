using Template.Api.Authentication;
using Template.Api.Features.Account;
using Template.Api.Features.Auth;
using Template.Api.Features.ApiKeys;
using Template.Api.Features.Collaboration;
using Template.Api.Features.Health;
using Template.Api.Features.Organizations;
using Template.Api.Features.System;

namespace Template.Api.Endpoints;

internal static class EndpointModuleExtensions
{
    internal static IServiceCollection AddEndpointModules(this IServiceCollection services)
    {
        services.AddSingleton<IEndpointModule, AuthEndpointModule>();
        services.AddScoped<ApiJsonRequestReader>();
        services.AddScoped<ExternalOAuthChallengeService>();
        services.AddSingleton<IEndpointModule, ExternalAuthEndpointModule>();
        services.AddSingleton<IEndpointModule, AccountEndpointModule>();
        services.AddSingleton<IEndpointModule, ApiKeyEndpointModule>();
        services.AddSingleton<IEndpointModule, OrganizationEndpointModule>();
        services.AddSingleton<IEndpointModule, TeamEndpointModule>();
        services.AddSingleton<IEndpointModule, InvitationEndpointModule>();
        services.AddSingleton<IEndpointModule, HealthEndpointModule>();
        services.AddSingleton<IEndpointModule, SystemEndpointModule>();
        return services;
    }

    internal static IEndpointRouteBuilder MapEndpointModules(
        this IEndpointRouteBuilder endpoints)
    {
        var context = new EndpointRouteContext(
            endpoints,
            endpoints.MapGroup("/api/v1")
                .RequireAuthorization(ApiPolicies.BrowserSession));

        foreach (var module in endpoints.ServiceProvider
                     .GetRequiredService<IEnumerable<IEndpointModule>>())
        {
            module.MapEndpoints(context);
        }

        return endpoints;
    }
}
