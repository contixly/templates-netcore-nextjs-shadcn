using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Template.Api.OpenApi;

internal sealed class OrganizationContractOperationTransformer
    : IOpenApiOperationTransformer
{
    private const string CanonicalUuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string OrganizationSlugPattern =
        "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    private static readonly IReadOnlyDictionary<string, int[]> ProblemStatusesByOperation =
        new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["GetOrganizations"] = [400, 401, 405, 500],
            ["CreateOrganization"] = [400, 401, 405, 409, 500],
            ["GetOrganizationByKey"] = [401, 404, 405, 409, 500],
            ["UpdateOrganization"] = [400, 401, 403, 404, 405, 409, 500],
            ["DeleteOrganization"] = [400, 401, 403, 404, 405, 409, 500],
            ["SetActiveOrganization"] = [400, 401, 404, 405, 409, 500],
            ["GetOrganizationMembers"] = [400, 401, 404, 405, 409, 500],
            ["AddOrganizationMember"] = [400, 401, 403, 404, 405, 409, 500],
            ["UpdateOrganizationMemberRole"] = [400, 401, 403, 404, 405, 409, 500]
        };

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId is null ||
            !ProblemStatusesByOperation.TryGetValue(
                operation.OperationId,
                out var problemStatuses))
        {
            return Task.CompletedTask;
        }

        ApplyExactProblemResponses(
            operation,
            context.Document!,
            problemStatuses);
        if (operation.OperationId is "CreateOrganization" or
            "AddOrganizationMember")
        {
            ApplyRequiredLocationHeader(operation);
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
            ApplyPaginationContract(
                operation,
                operation.OperationId == "GetOrganizations"
                    ? "Opaque versioned cursor returned by the preceding page. " +
                      "Organizations are ordered by the actor membership's immutable " +
                      "joinedAt and membership id."
                    : "Opaque versioned cursor returned by the preceding page. " +
                      "Members are ordered by immutable joinedAt and member id.");
            ApplyBadRequestVariants(operation, context.Document!);
        }

        return Task.CompletedTask;
    }

    private static void ApplyRequiredLocationHeader(OpenApiOperation operation)
    {
        if (operation.Responses?.TryGetValue(
                StatusCodes.Status201Created.ToString(),
                out var response) != true ||
            response is not OpenApiResponse created)
        {
            return;
        }

        created.Headers ??=
            new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        created.Headers["Location"] = new OpenApiHeader
        {
            Required = true,
            Description = "URI reference for the created resource.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uri-reference"
            }
        };
    }

    private static void ApplyExactProblemResponses(
        OpenApiOperation operation,
        OpenApiDocument document,
        IReadOnlyCollection<int> expectedStatuses)
    {
        operation.Responses ??= new OpenApiResponses();
        var expected = expectedStatuses
            .Select(status => status.ToString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var status in operation.Responses.Keys
                     .Where(status =>
                         int.TryParse(status, out var value) &&
                         value >= StatusCodes.Status400BadRequest &&
                         !expected.Contains(status))
                     .ToArray())
        {
            operation.Responses.Remove(status);
        }

        foreach (var status in expectedStatuses)
        {
            if (operation.Responses.ContainsKey(status.ToString()))
            {
                continue;
            }

            operation.Responses[status.ToString()] = new OpenApiResponse
            {
                Description = ReasonPhrase(status),
                Content = new Dictionary<string, OpenApiMediaType>(
                    StringComparer.Ordinal)
                {
                    [OpenApiDefaults.ProblemContentType] = new()
                    {
                        Schema = new OpenApiSchemaReference(
                            nameof(ProblemDetails),
                            document)
                    }
                }
            };
        }
    }

    private static string ReasonPhrase(int status) =>
        status switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status500InternalServerError => "Internal Server Error",
            _ => throw new InvalidOperationException(
                $"Unsupported organization problem status {status}.")
        };

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

        schema.Type = null;
        schema.Format = null;
        schema.MinLength = null;
        schema.MaxLength = null;
        schema.Pattern = null;
        schema.OneOf =
        [
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uuid",
                Pattern = CanonicalUuidPattern
            },
            new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                MinLength = 1,
                MaxLength = 64,
                Pattern = OrganizationSlugPattern,
                Not = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Pattern = CanonicalUuidPattern
                }
            }
        ];
        schema.Description =
            "Canonical organization UUID or lowercase non-UUID-shaped slug. " +
            "UUID keys resolve only by organization ID. The response canonicalKey " +
            "is always the preferred slug.";
    }

    private static void ApplyPaginationContract(
        OpenApiOperation operation,
        string cursorDescription)
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
            cursorSchema.Description = cursorDescription;
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
