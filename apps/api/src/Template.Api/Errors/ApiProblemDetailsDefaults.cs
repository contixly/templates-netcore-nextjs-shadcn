using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Template.Api.Observability;

namespace Template.Api.Errors;

internal static class ApiProblemDetailsDefaults
{
    internal static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;
        var isValidation = problem is HttpValidationProblemDetails;
        var definition = Resolve(status, isValidation);

        problem.Status = status;
        problem.Type = $"urn:template:problem:{definition.Code}";
        problem.Title = definition.Title;
        problem.Detail = definition.Detail;
        problem.Instance = context.HttpContext.Request.Path.Value ?? "/";
        problem.Extensions["code"] = definition.Code;
        problem.Extensions["traceId"] =
            CorrelationIdMiddleware.GetTraceId(context.HttpContext);

        if (problem is HttpValidationProblemDetails validation)
        {
            var normalized = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (key, messages) in validation.Errors)
            {
                var normalizedKey = string.Join(
                    '.',
                    key.Split('.')
                        .Select(JsonNamingPolicy.CamelCase.ConvertName));
                if (!normalized.TryGetValue(normalizedKey, out var mergedMessages))
                {
                    mergedMessages = [];
                    normalized[normalizedKey] = mergedMessages;
                }

                mergedMessages.AddRange(messages);
            }

            validation.Errors.Clear();
            foreach (var (key, messages) in normalized)
            {
                validation.Errors[key] = [.. messages];
            }
        }
    }

    private static ProblemDefinition Resolve(int status, bool isValidation) =>
        (status, isValidation) switch
        {
            (StatusCodes.Status400BadRequest, true) => new(
                ApiProblemCodes.ValidationFailed,
                "Request validation failed",
                "One or more validation errors occurred."),
            (StatusCodes.Status400BadRequest, false) => new(
                ApiProblemCodes.InvalidRequest,
                "Invalid request",
                "The request could not be processed."),
            (StatusCodes.Status401Unauthorized, _) => new(
                ApiProblemCodes.Unauthorized,
                "Authentication required",
                "Authentication is required to access this resource."),
            (StatusCodes.Status403Forbidden, _) => new(
                ApiProblemCodes.Forbidden,
                "Access forbidden",
                "You do not have permission to access this resource."),
            (StatusCodes.Status404NotFound, _) => new(
                ApiProblemCodes.NotFound,
                "Resource not found",
                "The requested resource was not found."),
            (StatusCodes.Status405MethodNotAllowed, _) => new(
                ApiProblemCodes.MethodNotAllowed,
                "Method not allowed",
                "The HTTP method is not supported for this resource."),
            _ when status >= StatusCodes.Status500InternalServerError => new(
                ApiProblemCodes.InternalError,
                "Internal server error",
                "An unexpected error occurred."),
            _ => new(
                ApiProblemCodes.InvalidRequest,
                "Invalid request",
                "The request could not be processed.")
        };

    private sealed record ProblemDefinition(string Code, string Title, string Detail);
}
