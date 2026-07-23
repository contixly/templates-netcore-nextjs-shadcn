using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Contracts;

namespace Template.Api.OpenApi;

internal sealed class ApiContractSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly string[] ProblemInvariantProperties =
    [
        "type",
        "title",
        "status",
        "detail",
        "instance",
        "code",
        "traceId"
    ];

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(ApiResponse<>))
        {
            MakePropertyRequiredAndNonNull(schema, "data");
        }

        if (type == typeof(ProblemDetails) ||
            type == typeof(HttpValidationProblemDetails))
        {
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties.TryAdd(
                "code",
                new OpenApiSchema { Type = JsonSchemaType.String });
            schema.Properties.TryAdd(
                "traceId",
                new OpenApiSchema { Type = JsonSchemaType.String });

            foreach (var propertyName in ProblemInvariantProperties)
            {
                MakePropertyRequiredAndNonNull(schema, propertyName);
            }

            if (type == typeof(HttpValidationProblemDetails))
            {
                MakePropertyRequiredAndNonNull(schema, "errors");
            }
        }

        return Task.CompletedTask;
    }

    private static void MakePropertyRequiredAndNonNull(
        OpenApiSchema schema,
        string propertyName)
    {
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add(propertyName);

        if (schema.Properties?.TryGetValue(propertyName, out var property) == true &&
            property is OpenApiSchema propertySchema)
        {
            if (propertySchema.Type is { } type)
            {
                propertySchema.Type = type & ~JsonSchemaType.Null;
            }
        }
    }
}
