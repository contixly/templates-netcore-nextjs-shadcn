using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Features.ApiKeys;
using Template.Api.Features.Collaboration;
using Template.Api.Features.Organizations;

namespace Template.Api.OpenApi;

internal sealed class ApiKeyContractSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string CanonicalUuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string AccessPrincipalDescription =
        "Principal used for organization access. user uses the current stored " +
        "membership role and capabilities; organization uses the non-membership " +
        "currentRole sentinel organization and has every browser mutation capability " +
        "set to false.";

    private static readonly string[] PresetIds =
    [
        "basic-read",
        "organization-read",
        "organization-members-read",
        "organization-teams-read",
        "organization-team-members-read",
        "organization-read-all"
    ];

    private static readonly string[] Scopes =
    [
        "basic:read",
        "organization:read",
        "member:read",
        "team:read",
        "teamMember:read"
    ];

    private static readonly string[] Expirations =
    [
        "never",
        "7d",
        "30d",
        "90d",
        "365d"
    ];

    private static readonly string[] RateLimitWindows = ["1m", "1h", "1d"];

    private static readonly string[] ApiKeyProblemCodes =
    [
        "api_key_not_found",
        "api_key_permission_denied",
        "api_key_update_unchanged",
        "api_key_missing",
        "api_key_invalid",
        "api_key_rate_limited",
        "organization_access_denied"
    ];

    private static readonly string[] CapabilityNames =
    [
        "canUpdateOrganization",
        "canDeleteOrganization",
        "canAddMembers",
        "canUpdateMemberRoles",
        "canManageTeams",
        "canManageInvitations",
        "canManageApiKeys"
    ];

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(CreateApiKeyRequest))
        {
            ApplyCreateRequest(schema);
        }

        if (type == typeof(UpdateApiKeyRequest))
        {
            ApplyUpdateRequest(schema);
        }

        if (type == typeof(ApiKeyResponse) ||
            type == typeof(ApiKeySecretResponse))
        {
            ApplyApiKeyProjection(schema);
        }

        if (type == typeof(ApiKeySecretResponse))
        {
            MakePropertyRequiredAndNonNull(schema, "key");
            if (Property(schema, "key") is { } key)
            {
                key.Description =
                    "Reveal-once credential. Store it securely now; it is never returned by " +
                    "list, update, revoke, /me, or resource-read operations.";
                key.Example = null;
                key.Default = null;
            }
        }

        if (type == typeof(ApiKeyRevocationResponse))
        {
            ApplyUuid(schema, "id");
        }

        if (type == typeof(ApiKeyMeResponse))
        {
            SetArrayItemEnum(schema, "scopes", Scopes);
        }

        if (type == typeof(ApiKeyMePrincipalResponse))
        {
            SetPropertyEnum(schema, "ownerKind", "user", "organization");
            ApplyUuid(schema, "userId");
            ApplyUuid(schema, "organizationId");
            ApplyApiKeyPrincipalDiscriminator(schema);
            if (Property(schema, "ownerKind") is { } ownerKind)
            {
                ownerKind.Description =
                    "user means a personal key; organization means an organization-owned key.";
            }
        }

        if (type == typeof(ApiKeyMeKeyResponse))
        {
            ApplyUuid(schema, "id");
            SetPropertyEnum(schema, "configId", "user-keys", "org-keys");
            if (Property(schema, "start") is { } start)
            {
                start.Description =
                    "Safe non-secret credential prefix for identification only.";
            }
        }

        if (type == typeof(OrganizationSummaryResponse) ||
            type == typeof(MachineOrganizationDetailResponse))
        {
            ApplyMachineOrganizationDiscriminator(schema);
        }

        if (type == typeof(OrganizationDetailResponse))
        {
            MakePropertyRequiredAndNonNull(schema, "accessPrincipal");
            SetPropertyEnum(schema, "accessPrincipal", "user");
        }

        if (type == typeof(TeamResponse))
        {
            MakePropertyRequiredAndNonNull(schema, "membersIncluded");
            if (Property(schema, "membersIncluded") is { } included)
            {
                included.Description =
                    "Whether the embedded members page is included. Browser reads return true; " +
                    "machine reads without teamMember:read return false with an empty embedded " +
                    "page while memberCount remains available.";
            }
        }

        if (type == typeof(ProblemDetails) ||
            type == typeof(HttpValidationProblemDetails))
        {
            AppendProblemCodes(schema);
        }

        return Task.CompletedTask;
    }

    private static void ApplyCreateRequest(OpenApiSchema schema)
    {
        foreach (var propertyName in new[]
                 {
                     "name",
                     "presetIds",
                     "expiresIn",
                     "rateLimitEnabled",
                     "rateLimitMax",
                     "rateLimitWindow"
                 })
        {
            MakePropertyRequiredAndNonNull(schema, propertyName);
        }

        ApplyName(schema);
        ApplyPresetIds(schema);
        SetPropertyEnum(schema, "expiresIn", Expirations);
        ApplyRateLimit(schema);
        if (Property(schema, "expiresIn") is { } expiration)
        {
            expiration.Description =
                "Closed expiry duration applied from successful creation; never has no expiry.";
        }
    }

    private static void ApplyUpdateRequest(OpenApiSchema schema)
    {
        foreach (var propertyName in new[]
                 {
                     "name",
                     "presetIds",
                     "expiresIn",
                     "enabled",
                     "rateLimitEnabled",
                     "rateLimitMax",
                     "rateLimitWindow"
                 })
        {
            MakePropertyNonNull(schema, propertyName);
        }

        schema.Required?.Clear();
        ApplyName(schema);
        ApplyPresetIds(schema);
        SetPropertyEnum(schema, "expiresIn", Expirations);
        ApplyRateLimit(schema);
        schema.Description =
            "Every supplied member is non-null. Omitted members are preserved; an empty or " +
            "semantically unchanged update returns 409 api_key_update_unchanged. Supplying " +
            "expiresIn restarts that duration from the successful update time.";
    }

    private static void ApplyName(OpenApiSchema schema)
    {
        if (Property(schema, "name") is not { } name)
        {
            return;
        }

        name.MinLength = null;
        name.MaxLength = null;
        AddExtension(name, "x-trimmed-min-length", 1);
        AddExtension(name, "x-trimmed-max-length", 32);
        AddExtension(name, "x-length-unit", "unicode-scalar");
        AddExtension(name, "x-control-characters", false);
        name.Description =
            "Trimmed before use; the result must contain 1 to 32 Unicode scalars and no control characters.";
    }

    private static void ApplyPresetIds(OpenApiSchema schema)
    {
        if (Property(schema, "presetIds") is not { Items: OpenApiSchema items } presets)
        {
            return;
        }

        presets.MinItems = 1;
        presets.Description =
            "Nonempty closed preset selection expanded server-side into canonical scopes; clients never submit raw scopes.";
        SetEnum(items, PresetIds);
    }

    private static void ApplyRateLimit(OpenApiSchema schema)
    {
        if (Property(schema, "rateLimitMax") is { } maximum)
        {
            maximum.Type = JsonSchemaType.Integer;
            maximum.Format = "int32";
            maximum.Pattern = null;
            maximum.Minimum = "1";
            maximum.Maximum = "1000000";
        }

        SetPropertyEnum(schema, "rateLimitWindow", RateLimitWindows);
        if (Property(schema, "rateLimitWindow") is { } window)
        {
            window.Description = "Closed fixed-window duration.";
        }
    }

    private static void ApplyApiKeyProjection(OpenApiSchema schema)
    {
        foreach (var propertyName in new[]
                 {
                     "id",
                     "ownerKind",
                     "ownerId",
                     "name",
                     "start",
                     "status",
                     "enabled",
                     "scopes",
                     "rateLimitEnabled",
                     "rateLimitMax",
                     "rateLimitWindow",
                     "requestCount",
                     "windowStartedAt",
                     "lastRequestAt",
                     "expiresAt",
                     "rotatedAt",
                     "createdAt",
                     "updatedAt"
                 })
        {
            MakePropertyRequired(schema, propertyName);
        }

        ApplyUuid(schema, "id");
        ApplyUuid(schema, "ownerId");
        SetPropertyEnum(schema, "ownerKind", "user", "organization");
        SetPropertyEnum(schema, "status", "active", "disabled", "expired");
        SetArrayItemEnum(schema, "scopes", Scopes);
        SetPropertyEnum(schema, "rateLimitWindow", RateLimitWindows);
        if (Property(schema, "name") is { } name)
        {
            name.MinLength = 1;
            name.MaxLength = 32;
        }

        if (Property(schema, "start") is { } start)
        {
            start.MinLength = 16;
            start.MaxLength = 16;
            start.Description =
                "Safe non-secret credential prefix for identification only.";
        }

        if (Property(schema, "rateLimitMax") is { } rateLimitMaximum)
        {
            rateLimitMaximum.Type = JsonSchemaType.Integer;
            rateLimitMaximum.Format = "int32";
            rateLimitMaximum.Pattern = null;
            rateLimitMaximum.Minimum = "1";
            rateLimitMaximum.Maximum = "1000000";
        }

        if (Property(schema, "requestCount") is { } requestCount)
        {
            requestCount.Type = JsonSchemaType.Integer;
            requestCount.Format = "int32";
            requestCount.Pattern = null;
            requestCount.Minimum = "0";
        }
    }

    private static void ApplyApiKeyPrincipalDiscriminator(OpenApiSchema schema)
    {
        schema.Discriminator = new OpenApiDiscriminator
        {
            PropertyName = "ownerKind"
        };
        schema.OneOf =
        [
            ObjectVariant(new Dictionary<string, OpenApiSchema>(
                StringComparer.Ordinal)
            {
                ["ownerKind"] = StringEnum("user"),
                ["userId"] = Uuid(),
                ["organizationId"] = Null()
            }),
            ObjectVariant(new Dictionary<string, OpenApiSchema>(
                StringComparer.Ordinal)
            {
                ["ownerKind"] = StringEnum("organization"),
                ["userId"] = Null(),
                ["organizationId"] = Uuid()
            })
        ];
    }

    private static void ApplyMachineOrganizationDiscriminator(OpenApiSchema schema)
    {
        MakePropertyRequiredAndNonNull(schema, "accessPrincipal");
        MakePropertyRequiredAndNonNull(schema, "currentRole");
        SetPropertyEnum(schema, "accessPrincipal", "user", "organization");
        SetPropertyEnum(
            schema,
            "currentRole",
            "member",
            "admin",
            "owner",
            "organization");
        if (Property(schema, "accessPrincipal") is { } principal)
        {
            principal.Description = AccessPrincipalDescription;
        }

        schema.Discriminator = new OpenApiDiscriminator
        {
            PropertyName = "accessPrincipal"
        };
        schema.OneOf =
        [
            OrganizationAccessVariant(
                "user",
                ["member", "admin", "owner"],
                literalFalseCapabilities: false),
            OrganizationAccessVariant(
                "organization",
                ["organization"],
                literalFalseCapabilities: true)
        ];
    }

    private static OpenApiSchema OrganizationAccessVariant(
        string accessPrincipal,
        string[] roles,
        bool literalFalseCapabilities) =>
        ObjectVariant(new Dictionary<string, OpenApiSchema>(
            StringComparer.Ordinal)
        {
            ["accessPrincipal"] = StringEnum(accessPrincipal),
            ["currentRole"] = StringEnum(roles),
            ["capabilities"] = Capabilities(literalFalseCapabilities)
        });

    private static OpenApiSchema Capabilities(bool literalFalse)
    {
        var properties = new Dictionary<string, OpenApiSchema>(
            StringComparer.Ordinal);
        foreach (var name in CapabilityNames)
        {
            properties[name] = new OpenApiSchema
            {
                Type = JsonSchemaType.Boolean,
                Enum = literalFalse ? [JsonValue.Create(false)!] : null
            };
        }

        return ObjectVariant(properties);
    }

    private static OpenApiSchema ObjectVariant(
        IReadOnlyDictionary<string, OpenApiSchema> properties) =>
        new()
        {
            Type = JsonSchemaType.Object,
            Required = properties.Keys.ToHashSet(StringComparer.Ordinal),
            Properties = properties.ToDictionary(
                property => property.Key,
                property => (IOpenApiSchema)property.Value,
                StringComparer.Ordinal)
        };

    private static OpenApiSchema StringEnum(params string[] values)
    {
        var schema = new OpenApiSchema();
        SetEnum(schema, values);
        return schema;
    }

    private static OpenApiSchema Uuid() => new()
    {
        Type = JsonSchemaType.String,
        Format = "uuid",
        Pattern = CanonicalUuidPattern
    };

    private static OpenApiSchema Null() => new()
    {
        Type = JsonSchemaType.Null
    };

    private static void AppendProblemCodes(OpenApiSchema schema)
    {
        if (Property(schema, "code") is not { } code)
        {
            return;
        }

        var values = code.Enum?.Select(value => value.GetValue<string>()).ToList()
            ?? [];
        foreach (var problemCode in ApiKeyProblemCodes)
        {
            if (!values.Contains(problemCode, StringComparer.Ordinal))
            {
                values.Add(problemCode);
            }
        }

        SetEnum(code, values.ToArray());
    }

    private static void ApplyUuid(OpenApiSchema schema, string propertyName)
    {
        if (Property(schema, propertyName) is not { } property)
        {
            return;
        }

        property.Format = "uuid";
        property.Pattern = CanonicalUuidPattern;
    }

    private static OpenApiSchema? Property(
        OpenApiSchema schema,
        string propertyName) =>
        schema.Properties?.TryGetValue(propertyName, out var property) == true
            ? property as OpenApiSchema
            : null;

    private static void SetPropertyEnum(
        OpenApiSchema schema,
        string propertyName,
        params string[] values)
    {
        if (Property(schema, propertyName) is { } property)
        {
            SetEnum(property, values);
        }
    }

    private static void SetArrayItemEnum(
        OpenApiSchema schema,
        string propertyName,
        params string[] values)
    {
        if (Property(schema, propertyName)?.Items is OpenApiSchema items)
        {
            SetEnum(items, values);
        }
    }

    private static void SetEnum(OpenApiSchema schema, params string[] values)
    {
        schema.Type = JsonSchemaType.String;
        schema.Enum = [.. values.Select(value => JsonValue.Create(value)!)];
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
        MakePropertyRequired(schema, propertyName);
        MakePropertyNonNull(schema, propertyName);
    }

    private static void MakePropertyRequired(
        OpenApiSchema schema,
        string propertyName)
    {
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add(propertyName);
    }

    private static void MakePropertyNonNull(
        OpenApiSchema schema,
        string propertyName)
    {
        if (Property(schema, propertyName) is { Type: { } type } property)
        {
            property.Type = type & ~JsonSchemaType.Null;
        }
    }
}
