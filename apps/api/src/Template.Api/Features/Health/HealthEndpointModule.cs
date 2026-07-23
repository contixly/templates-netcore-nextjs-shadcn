using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.OpenApi;

namespace Template.Api.Features.Health;

internal sealed class HealthEndpointModule : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapReady(endpoints, "/api/health", "GetHealth");
        MapLive(endpoints);
        MapReady(endpoints, "/api/health/ready", "GetReadiness");
    }

    private static void MapLive(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/health/live",
                (HealthCheckService checks, TimeProvider timeProvider,
                    HttpContext context, CancellationToken cancellationToken) =>
                    Evaluate(
                        checks,
                        timeProvider,
                        context,
                        _ => false,
                        cancellationToken))
            .AllowAnonymous()
            .WithName("GetLiveness")
            .WithSummary("Reports whether the API process can answer requests.")
            .Produces<ApiResponse<HealthResponse>>()
            .Produces<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable)
            .ProducesPublicApiProblems();
    }

    private static void MapReady(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationName)
    {
        endpoints.MapGet(
                route,
                (HealthCheckService checks, TimeProvider timeProvider,
                    HttpContext context, CancellationToken cancellationToken) =>
                    Evaluate(
                        checks,
                        timeProvider,
                        context,
                        registration => registration.Tags.Contains("ready"),
                        cancellationToken))
            .AllowAnonymous()
            .WithName(operationName)
            .WithSummary("Reports whether the API is ready to receive traffic.")
            .Produces<ApiResponse<HealthResponse>>()
            .Produces<ApiResponse<HealthResponse>>(StatusCodes.Status503ServiceUnavailable)
            .ProducesPublicApiProblems();
    }

    private static async Task<IResult> Evaluate(
        HealthCheckService checks,
        TimeProvider timeProvider,
        HttpContext context,
        Func<HealthCheckRegistration, bool> predicate,
        CancellationToken cancellationToken)
    {
        var report = await checks.CheckHealthAsync(predicate, cancellationToken);
        var statusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.Headers.CacheControl = "no-store";

        return Results.Json(
            new ApiResponse<HealthResponse>(
                new HealthResponse(
                    report.Status.ToString().ToLowerInvariant(),
                    timeProvider.GetUtcNow())),
            statusCode: statusCode);
    }
}
