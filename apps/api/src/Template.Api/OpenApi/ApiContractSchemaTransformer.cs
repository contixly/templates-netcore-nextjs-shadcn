using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Contracts;
using Template.Api.Features.Auth;

namespace Template.Api.OpenApi;

internal sealed class ApiContractSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string LocalAutomationEmailPattern =
        """^[lL][oO][cC][aA][lL]-[aA][gG][eE][nN][tT]\+[^@\s]+@[lL][oO][cC][aA][lL]-[aA][gG][eE][nN][tT]\.[tT][eE][sS][tT]$""";

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

        if (type == typeof(CreateLocalAutomationScenarioRequest))
        {
            ApplyScenarioInputConstraints(schema);
        }

        return Task.CompletedTask;
    }

    private static void ApplyScenarioInputConstraints(OpenApiSchema schema)
    {
        if (schema.Properties?.TryGetValue("name", out var nameProperty) == true &&
            nameProperty is OpenApiSchema name)
        {
            AddExtension(name, "x-trimmed-min-length", 2);
            AddExtension(name, "x-trimmed-max-length", 50);
            name.Description =
                "Trimmed before use; the trimmed name must contain 2 to 50 characters.";
        }

        if (schema.Properties?.TryGetValue("email", out var emailProperty) == true &&
            emailProperty is OpenApiSchema email)
        {
            AddExtension(email, "x-trimmed-max-length", 254);
            AddExtension(email, "x-trimmed-format", "email");
            AddExtension(email, "x-trimmed-pattern", LocalAutomationEmailPattern);
            email.Description =
                "Trimmed and lowercased before use; the trimmed value must be a valid email " +
                "of at most 254 characters in the case-insensitive " +
                "local-agent+...@local-agent.test namespace.";
        }
    }

    private static void AddExtension(
        OpenApiSchema schema,
        string name,
        JsonNode value)
    {
        schema.Extensions ??=
            new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        schema.Extensions[name] = new JsonNodeExtension(value);
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
