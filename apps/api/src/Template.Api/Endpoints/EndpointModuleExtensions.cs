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
        foreach (var module in endpoints.ServiceProvider
                     .GetRequiredService<IEnumerable<IEndpointModule>>())
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
