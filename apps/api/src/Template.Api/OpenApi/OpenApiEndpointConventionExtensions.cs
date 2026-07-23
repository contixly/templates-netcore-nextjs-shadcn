using Microsoft.AspNetCore.Mvc;

namespace Template.Api.OpenApi;

internal static class OpenApiEndpointConventionExtensions
{
    internal static RouteHandlerBuilder ProducesValidationProblem(
        this RouteHandlerBuilder builder) =>
        builder.Produces<HttpValidationProblemDetails>(
            StatusCodes.Status400BadRequest,
            OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesPublicApiProblems(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status404NotFound,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status405MethodNotAllowed,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status500InternalServerError,
                OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesProtectedApiProblems(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status401Unauthorized,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status403Forbidden,
                OpenApiDefaults.ProblemContentType)
            .ProducesPublicApiProblems();
}
