using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Template.Api.Errors;

internal sealed class ApiProblemDetailsWriter(
    IOptions<JsonOptions> jsonOptions,
    IOptions<ProblemDetailsOptions> problemDetailsOptions)
    : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) =>
        context.HttpContext.Request.Path.StartsWithSegments("/api");

    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        problemDetailsOptions.Value.CustomizeProblemDetails?.Invoke(context);

        var response = context.HttpContext.Response;
        response.ContentType = "application/problem+json";

        await JsonSerializer.SerializeAsync(
            response.Body,
            context.ProblemDetails,
            context.ProblemDetails.GetType(),
            jsonOptions.Value.SerializerOptions,
            context.HttpContext.RequestAborted);
    }
}
