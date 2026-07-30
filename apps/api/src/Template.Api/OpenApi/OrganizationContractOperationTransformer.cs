using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Template.Api.OpenApi;

internal sealed class OrganizationContractOperationTransformer
    : IOpenApiOperationTransformer
{
    private static readonly HashSet<string> OrganizationOperationIds =
    [
        "GetOrganizations",
        "CreateOrganization",
        "GetOrganizationByKey",
        "UpdateOrganization",
        "DeleteOrganization",
        "SetActiveOrganization",
        "GetOrganizationMembers",
        "AddOrganizationMember",
        "UpdateOrganizationMemberRole"
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId is null ||
            !OrganizationOperationIds.Contains(operation.OperationId))
        {
            return Task.CompletedTask;
        }

        ApplyPathParameterContract(operation, "organizationId", uuid: true);
        ApplyPathParameterContract(operation, "memberId", uuid: true);

        if (operation.OperationId == "GetOrganizationByKey")
        {
            ApplyOrganizationKeyContract(operation);
        }

        if (operation.OperationId is "GetOrganizations" or
            "GetOrganizationMembers")
        {
            ApplyPaginationContract(operation);
            ApplyBadRequestVariants(operation, context.Document!);
        }

        return Task.CompletedTask;
    }

    private static void ApplyPathParameterContract(
        OpenApiOperation operation,
        string parameterName,
        bool uuid)
    {
        var parameter = operation.Parameters?.SingleOrDefault(
            value =>
                value.In == ParameterLocation.Path &&
                string.Equals(
                    value.Name,
                    parameterName,
                    StringComparison.Ordinal));
        if (parameter?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Type = JsonSchemaType.String;
        if (uuid)
        {
            schema.Format = "uuid";
        }
    }

    private static void ApplyOrganizationKeyContract(OpenApiOperation operation)
    {
        var parameter = operation.Parameters?.SingleOrDefault(
            value =>
                value.In == ParameterLocation.Path &&
                string.Equals(
                    value.Name,
                    "organizationKey",
                    StringComparison.Ordinal));
        if (parameter?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Type = JsonSchemaType.String;
        schema.MinLength = 1;
        schema.MaxLength = 64;
        schema.Description =
            "Canonical organization UUID or lowercase slug. The response canonicalKey " +
            "is always the preferred slug.";
    }

    private static void ApplyPaginationContract(OpenApiOperation operation)
    {
        var limit = operation.Parameters?.SingleOrDefault(
            parameter => string.Equals(
                parameter.Name,
                "limit",
                StringComparison.Ordinal));
        if (limit?.Schema is OpenApiSchema limitSchema)
        {
            limitSchema.Type = JsonSchemaType.Integer;
            limitSchema.Format = "int32";
            limitSchema.Pattern = null;
            limitSchema.Minimum = "1";
            limitSchema.Maximum = "100";
            limitSchema.Default = JsonValue.Create(50);
        }

        var cursor = operation.Parameters?.SingleOrDefault(
            parameter => string.Equals(
                parameter.Name,
                "cursor",
                StringComparison.Ordinal));
        if (cursor?.Schema is OpenApiSchema cursorSchema)
        {
            cursorSchema.Type = JsonSchemaType.String;
            cursorSchema.Format = null;
            cursorSchema.Description =
                "Opaque versioned cursor returned by the preceding page.";
        }
    }

    private static void ApplyBadRequestVariants(
        OpenApiOperation operation,
        OpenApiDocument document)
    {
        if (operation.Responses?.TryGetValue(
                StatusCodes.Status400BadRequest.ToString(),
                out var response) != true ||
            response is not OpenApiResponse badRequest ||
            badRequest.Content?.TryGetValue(
                OpenApiDefaults.ProblemContentType,
                out var content) != true ||
            content is not OpenApiMediaType mediaType)
        {
            return;
        }

        mediaType.Schema = new OpenApiSchema
        {
            OneOf =
            [
                new OpenApiSchemaReference(
                    nameof(ProblemDetails),
                    document),
                new OpenApiSchemaReference(
                    nameof(HttpValidationProblemDetails),
                    document)
            ]
        };
    }
}
