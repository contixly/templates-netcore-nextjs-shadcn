using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Observability;

namespace Template.Api.Errors;

internal sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    private static readonly EventId UnhandledExceptionEvent =
        new(5000, "UnhandledApiException");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (
            int status,
            string code,
            IReadOnlyDictionary<string, string[]>? validationErrors,
            IReadOnlyDictionary<string, object?>? extensions) = exception switch
            {
                ApiValidationException validation => (
                    StatusCodes.Status400BadRequest,
                    ApiProblemCodes.ValidationFailed,
                    validation.Errors,
                    (IReadOnlyDictionary<string, object?>?)null),
                ApiProblemException problem => (
                    problem.StatusCode,
                    problem.Code,
                    (IReadOnlyDictionary<string, string[]>?)null,
                    problem.Extensions),
                AntiforgeryValidationException => (
                    StatusCodes.Status400BadRequest,
                    ApiProblemCodes.AntiforgeryFailed,
                    (IReadOnlyDictionary<string, string[]>?)null,
                    (IReadOnlyDictionary<string, object?>?)null),
                BadHttpRequestException badRequest => (
                    badRequest.StatusCode,
                    ApiProblemCodes.InvalidRequest,
                    (IReadOnlyDictionary<string, string[]>?)null,
                    (IReadOnlyDictionary<string, object?>?)null),
                _ => (
                    StatusCodes.Status500InternalServerError,
                    ApiProblemCodes.InternalError,
                    (IReadOnlyDictionary<string, string[]>?)null,
                    (IReadOnlyDictionary<string, object?>?)null)
            };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.Log(
                LogLevel.Error,
                UnhandledExceptionEvent,
                "Unhandled API exception {ExceptionType} for {Path} with trace {TraceId}",
                exception.GetType().FullName ?? exception.GetType().Name,
                RequestLoggingMiddleware.SafePath(httpContext),
                CorrelationIdMiddleware.GetTraceId(httpContext));
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
        if (extensions is not null)
        {
            CopySafeExtensions(details, extensions);
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = details
        });
    }

    private static void CopySafeExtensions(
        ProblemDetails details,
        IReadOnlyDictionary<string, object?> extensions)
    {
        foreach (var (name, value) in extensions)
        {
            if (name is not "email" and not "emailDomain"
                and not "allowedEmailDomains")
            {
                continue;
            }

            if (value is null or string or bool
                or byte or sbyte or short or ushort or int or uint
                or long or ulong or float or double or decimal)
            {
                details.Extensions[name] = value;
                continue;
            }

            if (value is IReadOnlyList<string> strings)
            {
                details.Extensions[name] = strings.ToArray();
            }
        }
    }
}
