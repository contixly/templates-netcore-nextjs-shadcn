using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Features.Collaboration;

namespace Template.Api.OpenApi;

internal sealed class CollaborationContractSchemaTransformer : IOpenApiSchemaTransformer
{
    internal const string CanonicalUuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string TeamNamePattern = @"^[\p{L}\p{Nd} _-]+$";
    private const string InvitationPathPattern =
        "^/invite/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private static readonly string[] OrganizationRoles = ["member", "admin", "owner"];
    private static readonly string[] InvitationStatuses = ["pending", "accepted", "rejected", "canceled"];
    private static readonly string[] InvitationDisplayStates = ["pending", "accepted", "rejected", "canceled", "expired"];
    private static readonly string[] InvitationDecisionStates =
    [
        "pending", "accepted", "rejected", "canceled", "expired", "recipient-mismatch",
        "email-verification-required", "domain-restricted", "already-member"
    ];

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        switch (context.JsonTypeInfo.Type)
        {
            case var type when type == typeof(TeamNameRequest):
                Strict(schema);
                Required(schema, "name");
                TrimmedTeamName(schema, "name");
                break;
            case var type when type == typeof(AddTeamMemberRequest):
                Strict(schema);
                Required(schema, "userId");
                Uuid(schema, "userId");
                break;
            case var type when type == typeof(CreateInvitationRequest):
                Strict(schema);
                Required(schema, "email");
                Required(schema, "role");
                TrimmedEmail(schema, "email");
                Enum(schema, "role", OrganizationRoles);
                Uuid(schema, "teamId", nullable: true);
                break;
            case var type when type == typeof(TeamResponse):
                Uuid(schema, "id");
                Uuid(schema, "organizationId");
                ProjectTeamName(schema, "name");
                Int32(schema, "memberCount", "0");
                break;
            case var type when type == typeof(TeamMemberResponse):
                Uuid(schema, "id");
                Uuid(schema, "userId");
                Role(schema, "role");
                Email(schema, "email");
                HttpsUri(schema, "imageUrl");
                break;
            case var type when type == typeof(TeamCandidateResponse):
                Uuid(schema, "memberId");
                Uuid(schema, "userId");
                Role(schema, "role");
                Email(schema, "email");
                HttpsUri(schema, "imageUrl");
                break;
            case var type when type == typeof(TeamDeletionResponse):
                Uuid(schema, "teamId");
                break;
            case var type when type == typeof(TeamMemberRemovalResponse):
                Uuid(schema, "teamId");
                Uuid(schema, "userId");
                break;
            case var type when type == typeof(InvitationResponse):
                Invitation(schema);
                break;
            case var type when type == typeof(InvitationDecisionResponse):
                Enum(schema, "state", InvitationDecisionStates);
                break;
            case var type when type == typeof(AcceptedInvitationResponse):
                Uuid(schema, "invitationId");
                Uuid(schema, "organizationId");
                break;
        }

        return Task.CompletedTask;
    }

    private static void Invitation(OpenApiSchema schema)
    {
        Uuid(schema, "id");
        Uuid(schema, "organizationId");
        Uuid(schema, "teamId", nullable: true);
        Uuid(schema, "inviterId");
        Email(schema, "email");
        Role(schema, "role");
        Enum(schema, "status", InvitationStatuses);
        Enum(schema, "displayState", InvitationDisplayStates);
        if (Property(schema, "invitationPath") is { } path)
        {
            path.Pattern = InvitationPathPattern;
            path.Description = "Relative same-origin invitation path.";
        }
    }

    private static void Strict(OpenApiSchema schema) => schema.AdditionalPropertiesAllowed = false;

    private static void Required(OpenApiSchema schema, string name)
    {
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add(name);
        if (Property(schema, name) is { Type: { } type } property)
        {
            property.Type = type & ~JsonSchemaType.Null;
        }
    }

    private static void Uuid(OpenApiSchema schema, string name, bool nullable = false)
    {
        if (Property(schema, name) is not { } property)
        {
            return;
        }

        property.Type = nullable ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
        property.Format = "uuid";
        property.Pattern = CanonicalUuidPattern;
    }

    private static void Role(OpenApiSchema schema, string name) => Enum(schema, name, OrganizationRoles);

    private static void Enum(OpenApiSchema schema, string name, params string[] values)
    {
        if (Property(schema, name) is not { } property)
        {
            return;
        }

        property.Type = JsonSchemaType.String;
        property.Enum = [.. values.Select(value => JsonValue.Create(value)! )];
    }

    private static void TrimmedTeamName(OpenApiSchema schema, string name)
    {
        if (Property(schema, name) is not { } property)
        {
            return;
        }

        property.MinLength = null;
        property.MaxLength = null;
        property.Pattern = null;
        Extension(property, "x-trimmed-min-length", 1);
        Extension(property, "x-trimmed-max-length", 50);
        Extension(property, "x-trimmed-pattern", TeamNamePattern);
        property.Description = "Trimmed before use; the trimmed name must contain 1 to 50 Unicode letters, digits, ordinary spaces, hyphens, or underscores.";
    }

    private static void ProjectTeamName(OpenApiSchema schema, string name)
    {
        if (Property(schema, name) is { } property)
        {
            property.MinLength = 1;
            property.MaxLength = 50;
            property.Pattern = TeamNamePattern;
        }
    }

    private static void TrimmedEmail(OpenApiSchema schema, string name)
    {
        if (Property(schema, name) is not { } property)
        {
            return;
        }

        property.MaxLength = null;
        property.Format = null;
        Extension(property, "x-trimmed-max-length", 254);
        Extension(property, "x-trimmed-format", "email");
        property.Description = "Trimmed and lowercased before use; the trimmed value must be a valid email of at most 254 characters.";
    }

    private static void Email(OpenApiSchema schema, string name)
    {
        if (Property(schema, name) is { } property)
        {
            property.Format = "email";
            property.MaxLength = 254;
        }
    }

    private static void HttpsUri(OpenApiSchema schema, string name)
    {
        if (Property(schema, name) is { } property)
        {
            property.Format = "uri";
            Extension(property, "x-uri-scheme", "https");
        }
    }

    private static void Int32(OpenApiSchema schema, string name, string minimum)
    {
        if (Property(schema, name) is { } property)
        {
            property.Type = JsonSchemaType.Integer;
            property.Format = "int32";
            property.Pattern = null;
            property.Minimum = minimum;
        }
    }

    private static OpenApiSchema? Property(OpenApiSchema schema, string name) =>
        schema.Properties?.TryGetValue(name, out var property) == true
            ? property as OpenApiSchema
            : null;

    private static void Extension(OpenApiSchema schema, string name, JsonNode value)
    {
        schema.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        schema.Extensions[name] = new JsonNodeExtension(value);
    }
}
