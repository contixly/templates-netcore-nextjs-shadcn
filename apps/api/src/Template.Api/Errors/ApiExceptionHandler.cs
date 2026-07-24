using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Template.Api.Errors;

internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, code, validationErrors) = exception switch
        {
            ApiValidationException validation => (
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.ValidationFailed,
                validation.Errors),
            ApiProblemException problem => (
                problem.StatusCode,
                problem.Code,
                null),
            AntiforgeryValidationException => (
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.AntiforgeryFailed,
                null),
            BadHttpRequestException badRequest => (
                badRequest.StatusCode,
                ApiProblemCodes.InvalidRequest,
                null),
            _ => (
                StatusCodes.Status500InternalServerError,
                ApiProblemCodes.InternalError,
                null)
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception");
        }
        else
        {
            logger.LogWarning("API request rejected with {Code}", code);
        }

        httpContext.Response.StatusCode = status;
        var details = validationErrors is null
            ? new ProblemDetails { Status = status }
            : new HttpValidationProblemDetails(
                validationErrors.ToDictionary(pair => pair.Key, pair => pair.Value))
            {
                Status = status
            };
        details.Extensions["code"] = code;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details
        });
    }
}
