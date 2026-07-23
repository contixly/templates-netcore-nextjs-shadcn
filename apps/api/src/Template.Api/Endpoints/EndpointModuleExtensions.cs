using Template.Api.Authentication;
using Template.Api.Features.Health;
using Template.Api.Features.System;

namespace Template.Api.Endpoints;

internal static class EndpointModuleExtensions
{
    internal static IServiceCollection AddEndpointModules(this IServiceCollection services)
    {
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
                .RequireAuthorization(ApiPolicies.Authenticated));

        foreach (var module in endpoints.ServiceProvider
                     .GetRequiredService<IEnumerable<IEndpointModule>>())
        {
            module.MapEndpoints(context);
        }

        return endpoints;
    }
}
