using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Features.Documents;

namespace Template.Api.OpenApi;

internal sealed class DocumentSearchContractSchemaTransformer
    : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(DocumentSearchResponse))
        {
            Require(schema, "pages", "headings");
        }
        else if (type == typeof(DocumentSearchPageResponse))
        {
            Require(
                schema,
                "type",
                "title",
                "description",
                "href",
                "group",
                "parentItem");
            SetTypeDiscriminator(schema, "page");
        }
        else if (type == typeof(DocumentSearchHeadingResponse))
        {
            Require(
                schema,
                "type",
                "title",
                "href",
                "pageTitle",
                "group",
                "parentItem");
            SetTypeDiscriminator(schema, "heading");
        }

        return Task.CompletedTask;
    }

    private static void Require(OpenApiSchema schema, params string[] names)
    {
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            schema.Required.Add(name);
            if (schema.Properties?.TryGetValue(name, out var property) == true &&
                property is OpenApiSchema propertySchema &&
                propertySchema.Type is { } type)
            {
                propertySchema.Type = type & ~JsonSchemaType.Null;
            }
        }
    }

    private static void SetTypeDiscriminator(OpenApiSchema schema, string value)
    {
        if (schema.Properties?.TryGetValue("type", out var property) != true ||
            property is not OpenApiSchema type)
        {
            return;
        }

        type.Type = JsonSchemaType.String;
        type.Enum = [JsonValue.Create(value)!];
    }
}
