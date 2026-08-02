using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Features.Documents;

namespace Template.Api.OpenApi;

internal sealed class DocumentSearchContractOperationTransformer
    : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                operation.OperationId,
                "SearchDocumentsSystem",
                StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        operation.Security = [];
        operation.Extensions ??=
            new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions["x-cache-control"] =
            new JsonNodeExtension(JsonValue.Create("no-store")!);

        ApplyQueryContract(operation);
        ApplyExactResponses(operation, context.Document!);
        return Task.CompletedTask;
    }

    private static void ApplyQueryContract(OpenApiOperation operation)
    {
        var query = operation.Parameters?.SingleOrDefault(parameter =>
            parameter.In == ParameterLocation.Query &&
            string.Equals(parameter.Name, "q", StringComparison.Ordinal))
            as OpenApiParameter;
        if (query?.Schema is OpenApiSchema querySchema)
        {
            query.Required = false;
            querySchema.Type = JsonSchemaType.String;
            querySchema.MaxLength = DocumentSearchEndpointBoundary.MaximumQueryLength;
        }

        var locale = operation.Parameters?.SingleOrDefault(parameter =>
            parameter.In == ParameterLocation.Query &&
            string.Equals(parameter.Name, "locale", StringComparison.Ordinal))
            as OpenApiParameter;
        if (locale?.Schema is OpenApiSchema localeSchema)
        {
            locale.Required = false;
            localeSchema.Type = JsonSchemaType.String;
            localeSchema.Enum =
            [
                JsonValue.Create("en")!,
                JsonValue.Create("ru")!
            ];
        }
    }

    private static void ApplyExactResponses(
        OpenApiOperation operation,
        OpenApiDocument document)
    {
        operation.Responses ??= new OpenApiResponses();
        var expectedStatuses = new HashSet<string>(
            ["200", "400", "406", "500"],
            StringComparer.Ordinal);
        foreach (var status in operation.Responses.Keys
                     .Where(status => !expectedStatuses.Contains(status))
                     .ToArray())
        {
            operation.Responses.Remove(status);
        }

        SetProblemResponse(
            operation,
            document,
            StatusCodes.Status400BadRequest,
            "Bad Request",
            nameof(HttpValidationProblemDetails));
        SetProblemResponse(
            operation,
            document,
            StatusCodes.Status406NotAcceptable,
            "Not Acceptable",
            nameof(ProblemDetails));
        SetProblemResponse(
            operation,
            document,
            StatusCodes.Status500InternalServerError,
            "Internal Server Error",
            nameof(ProblemDetails));

        foreach (var response in operation.Responses.Values.OfType<OpenApiResponse>())
        {
            response.Headers ??=
                new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Required = true,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("no-store")!]
                }
            };
        }
    }

    private static void SetProblemResponse(
        OpenApiOperation operation,
        OpenApiDocument document,
        int status,
        string description,
        string schemaName)
    {
        operation.Responses![status.ToString()] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                [OpenApiDefaults.ProblemContentType] = new()
                {
                    Schema = new OpenApiSchemaReference(schemaName, document)
                }
            }
        };
    }
}
