using Microsoft.AspNetCore.Mvc;

namespace Template.Api.OpenApi;

internal sealed record BadRequestVariantsMetadata;

internal static class OpenApiEndpointConventionExtensions
{
    internal static RouteHandlerBuilder ProducesValidationProblem(
        this RouteHandlerBuilder builder) =>
        builder.Produces<HttpValidationProblemDetails>(
            StatusCodes.Status400BadRequest,
            OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesBadRequestProblem(
        this RouteHandlerBuilder builder) =>
        builder.Produces<ProblemDetails>(
            StatusCodes.Status400BadRequest,
            OpenApiDefaults.ProblemContentType);

    internal static RouteHandlerBuilder ProducesBadRequestVariants(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status400BadRequest,
                OpenApiDefaults.ProblemContentType)
            .Produces<HttpValidationProblemDetails>(
                StatusCodes.Status400BadRequest,
                OpenApiDefaults.ProblemContentType)
            .WithMetadata(new BadRequestVariantsMetadata());

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

    internal static RouteHandlerBuilder ProducesLocalCreateProblems(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status409Conflict,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status429TooManyRequests,
                OpenApiDefaults.ProblemContentType)
            .ProducesPublicApiProblems();

    internal static RouteHandlerBuilder ProducesLocalSignInProblems(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<ProblemDetails>(
                StatusCodes.Status401Unauthorized,
                OpenApiDefaults.ProblemContentType)
            .Produces<ProblemDetails>(
                StatusCodes.Status429TooManyRequests,
                OpenApiDefaults.ProblemContentType)
            .ProducesPublicApiProblems();
}
