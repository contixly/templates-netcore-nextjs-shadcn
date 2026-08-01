using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Template.Api.OpenApi;

internal sealed class CollaborationContractOperationTransformer
    : IOpenApiOperationTransformer
{
    private static readonly IReadOnlyDictionary<string, int[]> ProblemStatuses =
        new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["GetTeams"] = [400, 401, 403, 404, 405, 409, 500],
            ["CreateTeam"] = [400, 401, 403, 404, 405, 409, 500],
            ["UpdateTeam"] = [400, 401, 403, 404, 405, 409, 500],
            ["DeleteTeam"] = [400, 401, 403, 404, 405, 409, 500],
            ["GetTeamMembers"] = [400, 401, 403, 404, 405, 409, 500],
            ["AddTeamMember"] = [400, 401, 403, 404, 405, 409, 500],
            ["RemoveTeamMember"] = [400, 401, 403, 404, 405, 409, 500],
            ["GetTeamMemberCandidates"] = [400, 401, 403, 404, 405, 409, 500],
            ["GetOrganizationInvitations"] = [400, 401, 403, 404, 405, 409, 500],
            ["CreateInvitation"] = [400, 401, 403, 404, 405, 409, 429, 500],
            ["GetAccountInvitations"] = [400, 401, 403, 404, 405, 409, 500],
            ["GetInvitationDecision"] = [400, 401, 403, 404, 405, 409, 500],
            ["AcceptInvitation"] = [400, 401, 403, 404, 405, 409, 429, 500],
            ["RejectInvitation"] = [400, 401, 403, 404, 405, 409, 429, 500],
            ["ConfirmLocalAutomationEmail"] = [400, 401, 403, 404, 405, 500]
        };

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId is null ||
            !ProblemStatuses.TryGetValue(operation.OperationId, out var statuses))
        {
            return Task.CompletedTask;
        }

        ApplyExactProblemResponses(operation, context.Document!, statuses);
        ApplyNoStoreResponseHeaders(operation);
        if (operation.OperationId is "CreateInvitation" or "AcceptInvitation" or
            "RejectInvitation")
        {
            ApplyRetryAfterHeader(operation);
        }
        ApplyUuidParameter(operation, "organizationId");
        ApplyUuidParameter(operation, "teamId");
        ApplyUuidParameter(operation, "userId");
        ApplyUuidParameter(operation, "invitationId");

        if (operation.OperationId is "GetTeams" or "GetTeamMembers" or
            "GetTeamMemberCandidates" or "GetOrganizationInvitations" or
            "GetAccountInvitations")
        {
            ApplyPagination(operation);
            ApplyBadRequestVariants(operation, context.Document!);
        }

        if (operation.OperationId is "AcceptInvitation" or "RejectInvitation")
        {
            // Empty-body enforcement can emit ProblemDetails while the endpoint
            // also advertises framework validation failures.
            ApplyBadRequestVariants(operation, context.Document!);
        }

        if (operation.OperationId == "GetTeamMemberCandidates")
        {
            var query = Parameter(operation, "q");
            if (query?.Schema is OpenApiSchema schema)
            {
                schema.Type = JsonSchemaType.String;
                schema.MaxLength = 100;
                schema.Description =
                    "Optional trimmed case-insensitive name or email search, at most 100 characters.";
            }
        }

        if (operation.OperationId == "GetOrganizationInvitations")
        {
            var status = Parameter(operation, "status");
            if (status?.Schema is OpenApiSchema schema)
            {
                SetStringEnum(schema, "pending", "accepted", "rejected", "canceled", "expired");
                schema.Description =
                    "Optional invitation display-state filter. Omit to return all activity.";
            }
        }

        if (operation.OperationId is "CreateTeam" or "AddTeamMember" or
            "CreateInvitation")
        {
            ApplyRequiredLocationHeader(operation);
        }

        return Task.CompletedTask;
    }

    private static void ApplyExactProblemResponses(
        OpenApiOperation operation,
        OpenApiDocument document,
        IReadOnlyCollection<int> expectedStatuses)
    {
        operation.Responses ??= new OpenApiResponses();
        var expected = expectedStatuses.Select(value => value.ToString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var status in operation.Responses.Keys.Where(status =>
                     int.TryParse(status, out var value) && value >= 400 &&
                     !expected.Contains(status)).ToArray())
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
                Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                {
                    [OpenApiDefaults.ProblemContentType] = new()
                    {
                        Schema = new OpenApiSchemaReference(nameof(ProblemDetails), document)
                    }
                }
            };
        }
    }

    private static void ApplyNoStoreResponseHeaders(OpenApiOperation operation)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions["x-cache-control"] = new JsonNodeExtension(JsonValue.Create("no-store")!);
        foreach (var response in operation.Responses?.Values.OfType<OpenApiResponse>() ?? [])
        {
            response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Required = true,
                Description = "All responses are non-cacheable.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create("no-store")!] }
            };
        }
    }

    private static void ApplyRequiredLocationHeader(OpenApiOperation operation)
    {
        if (operation.Responses?.TryGetValue("201", out var response) != true ||
            response is not OpenApiResponse created)
        {
            return;
        }

        created.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        created.Headers["Location"] = new OpenApiHeader
        {
            Required = true,
            Description = "URI reference for the created resource.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" }
        };
    }

    private static void ApplyRetryAfterHeader(OpenApiOperation operation)
    {
        if (operation.Responses?.TryGetValue("429", out var response) != true ||
            response is not OpenApiResponse rateLimited)
        {
            return;
        }

        rateLimited.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        rateLimited.Headers["Retry-After"] = new OpenApiHeader
        {
            Required = true,
            Description = "Decimal integer seconds until the caller may retry.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = "^[0-9]+$"
            }
        };
    }

    private static void ApplyPagination(OpenApiOperation operation)
    {
        if (Parameter(operation, "limit")?.Schema is OpenApiSchema limit)
        {
            limit.Type = JsonSchemaType.Integer;
            limit.Format = "int32";
            limit.Pattern = null;
            limit.Minimum = "1";
            limit.Maximum = "100";
            limit.Default = JsonValue.Create(50);
        }

        if (Parameter(operation, "cursor")?.Schema is OpenApiSchema cursor)
        {
            cursor.Type = JsonSchemaType.String;
            cursor.Format = null;
            cursor.Description = "Opaque versioned cursor.";
        }
    }

    private static void ApplyUuidParameter(OpenApiOperation operation, string name)
    {
        if (Parameter(operation, name)?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Type = JsonSchemaType.String;
        schema.Format = "uuid";
        schema.Pattern = CollaborationContractSchemaTransformer.CanonicalUuidPattern;
    }

    private static IOpenApiParameter? Parameter(OpenApiOperation operation, string name) =>
        operation.Parameters?.SingleOrDefault(value =>
            string.Equals(value.Name, name, StringComparison.Ordinal));

    private static void ApplyBadRequestVariants(OpenApiOperation operation, OpenApiDocument document)
    {
        if (operation.Responses?.TryGetValue("400", out var response) != true ||
            response is not OpenApiResponse badRequest ||
            badRequest.Content?.TryGetValue(OpenApiDefaults.ProblemContentType, out var content) != true ||
            content is not OpenApiMediaType media)
        {
            return;
        }

        media.Schema = new OpenApiSchema
        {
            OneOf =
            [
                new OpenApiSchemaReference(nameof(ProblemDetails), document),
                new OpenApiSchemaReference(nameof(HttpValidationProblemDetails), document)
            ]
        };
    }

    private static void SetStringEnum(OpenApiSchema schema, params string[] values)
    {
        schema.Type = JsonSchemaType.String;
        schema.Enum = [.. values.Select(value => JsonValue.Create(value)! )];
    }

    private static string ReasonPhrase(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => throw new InvalidOperationException($"Unsupported collaboration problem status {status}.")
    };
}
