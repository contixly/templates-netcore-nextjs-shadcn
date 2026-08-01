using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Features.Organizations;

namespace Template.Api.OpenApi;

internal sealed class OrganizationContractSchemaTransformer
    : IOpenApiSchemaTransformer
{
    private const string CanonicalUuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string OrganizationNamePattern =
        """^[\p{L}\p{Nd} _-]+$""";
    private const string OrganizationSlugPattern =
        "^[a-z0-9]+(?:-[a-z0-9]+)*$";
    private const string EmailDomainPattern =
        "^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\.)+" +
        "[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$";

    private static readonly string[] OrganizationRoles =
    [
        "member",
        "admin",
        "owner"
    ];

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(CreateOrganizationRequest))
        {
            MakePropertyRequiredAndNonNull(schema, "name");
            ApplyTrimmedOrganizationName(schema, "name");
        }

        if (type == typeof(UpdateOrganizationRequest))
        {
            schema.MinProperties = 1;
            schema.AnyOf =
            [
                RequiredNonNullProperty("name", JsonSchemaType.String),
                RequiredNonNullProperty("slug", JsonSchemaType.String),
                RequiredNonNullProperty(
                    "allowedEmailDomains",
                    JsonSchemaType.Array,
                    new OpenApiSchema { Type = JsonSchemaType.String })
            ];
            ApplyTrimmedOrganizationName(schema, "name");
            ApplyTrimmedSlug(schema, "slug");
            ApplyTrimmedEmailDomainArray(schema, "allowedEmailDomains");
        }

        if (type == typeof(DeleteOrganizationRequest))
        {
            MakePropertyRequiredAndNonNull(schema, "confirmationName");
            if (GetPropertySchema(schema, "confirmationName") is { } confirmation)
            {
                confirmation.MinLength = 1;
                confirmation.MaxLength = 50;
                confirmation.Description =
                    "Case-sensitive organization name confirmation.";
            }
        }

        if (type == typeof(SetActiveOrganizationRequest))
        {
            MakePropertyRequiredAndNonNull(schema, "organizationId");
            ApplyUuid(schema, "organizationId");
        }

        if (type == typeof(AddOrganizationMemberRequest))
        {
            MakePropertyRequiredAndNonNull(schema, "userId");
            MakePropertyRequiredAndNonNull(schema, "role");
            ApplyUuid(schema, "userId");
            SetPropertyStringEnum(schema, "role", OrganizationRoles);
        }

        if (type == typeof(UpdateOrganizationMemberRoleRequest))
        {
            MakePropertyRequiredAndNonNull(schema, "role");
            SetPropertyStringEnum(schema, "role", OrganizationRoles);
        }

        if (type == typeof(OrganizationSummaryResponse))
        {
            ApplyOrganizationProjection(schema);
        }

        if (type == typeof(OrganizationDetailResponse))
        {
            ApplyOrganizationProjection(schema);
            ApplyEmailDomainProjectionArray(schema, "allowedEmailDomains");
        }

        if (type == typeof(OrganizationMemberResponse))
        {
            ApplyUuid(schema, "id");
            ApplyUuid(schema, "userId");
            SetPropertyStringEnum(schema, "role", OrganizationRoles);
            ApplyEmailProjection(schema, "email");
            ApplyHttpsUriProjection(schema, "imageUrl");
            ApplyEmailDomainProjection(schema, "emailDomain");
        }

        if (type == typeof(OrganizationDeletionResponse) ||
            type == typeof(ActiveOrganizationResponse))
        {
            ApplyUuid(schema, "organizationId");
        }

        if (type == typeof(ProblemDetails) ||
            type == typeof(HttpValidationProblemDetails))
        {
            ApplyOrganizationProblemExtensions(schema);
        }

        return Task.CompletedTask;
    }

    private static void ApplyOrganizationProjection(OpenApiSchema schema)
    {
        ApplyUuid(schema, "id");
        SetPropertyStringEnum(schema, "currentRole", OrganizationRoles);

        if (GetPropertySchema(schema, "name") is { } name)
        {
            name.MinLength = 1;
            name.MaxLength = 50;
            name.Pattern = OrganizationNamePattern;
        }

        ApplyCanonicalSlug(schema, "slug");
        ApplyCanonicalSlug(schema, "canonicalKey");
    }

    private static void ApplyTrimmedOrganizationName(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is not { } name)
        {
            return;
        }

        name.MinLength = null;
        name.MaxLength = null;
        name.Pattern = null;
        AddExtension(name, "x-trimmed-min-length", 1);
        AddExtension(name, "x-trimmed-max-length", 50);
        AddExtension(name, "x-trimmed-pattern", OrganizationNamePattern);
        name.Description =
            "Trimmed before use; the trimmed name must contain 1 to 50 Unicode " +
            "letters, digits, ordinary spaces, hyphens, or underscores.";
    }

    private static void ApplyTrimmedSlug(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is not { } slug)
        {
            return;
        }

        slug.MinLength = null;
        slug.MaxLength = null;
        slug.Pattern = null;
        AddExtension(slug, "x-trimmed-min-length", 1);
        AddExtension(slug, "x-trimmed-max-length", 64);
        AddExtension(slug, "x-trimmed-pattern", OrganizationSlugPattern);
        AddExtension(slug, "x-trimmed-not-pattern", CanonicalUuidPattern);
        slug.Description =
            "Trimmed and lowercased before use; the normalized slug must contain " +
            "1 to 64 lowercase ASCII letters or digits separated by single hyphens " +
            "and must not be UUID-shaped.";
    }

    private static void ApplyTrimmedEmailDomainArray(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is not { } domains ||
            domains.Items is not OpenApiSchema domain)
        {
            return;
        }

        domains.MaxItems =
            OrganizationContractLimits.MaximumAllowedEmailDomains;
        domain.MaxLength = null;
        domain.Pattern = null;
        AddExtension(domain, "x-trimmed-max-length", 253);
        AddExtension(domain, "x-trimmed-format", "email-domain");
        AddExtension(domain, "x-trimmed-pattern", EmailDomainPattern);
        domain.Description =
            "Trimmed, lowercased, and stripped of at most one leading @ before " +
            "validation as an exact DNS-like email domain of at most 253 characters.";
    }

    private static void ApplyEmailDomainProjectionArray(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName)?.Items is OpenApiSchema domain)
        {
            ApplyEmailDomainProjection(domain);
        }
    }

    private static void ApplyEmailDomainProjection(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is { } domain)
        {
            ApplyEmailDomainProjection(domain);
        }
    }

    private static void ApplyEmailDomainProjection(OpenApiSchema domain)
    {
        domain.MaxLength = 253;
        domain.Pattern = EmailDomainPattern;
        AddExtension(domain, "x-format", "email-domain");
    }

    private static void ApplyCanonicalSlug(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is not { } slug)
        {
            return;
        }

        slug.MinLength = 1;
        slug.MaxLength = 64;
        slug.Pattern = OrganizationSlugPattern;
        slug.Not = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Pattern = CanonicalUuidPattern
        };
    }

    private static void ApplyEmailProjection(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is { } email)
        {
            email.Format = "email";
            email.MaxLength = 254;
        }
    }

    private static void ApplyHttpsUriProjection(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is not { } uri)
        {
            return;
        }

        uri.Format = "uri";
        AddExtension(uri, "x-uri-scheme", "https");
    }

    private static void ApplyOrganizationProblemExtensions(OpenApiSchema schema)
    {
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>(
            StringComparer.Ordinal);
        schema.Properties.TryAdd(
            "email",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                Format = "email",
                MaxLength = 254,
                Description =
                    "Target email for member_domain_acknowledgement_required."
            });
        schema.Properties.TryAdd(
            "emailDomain",
            new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                MaxLength = 253,
                Pattern = EmailDomainPattern,
                Description =
                    "Normalized target domain for member_domain_acknowledgement_required."
            });
        schema.Properties.TryAdd(
            "allowedEmailDomains",
            new OpenApiSchema
            {
                Type = JsonSchemaType.Array | JsonSchemaType.Null,
                Items = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    MaxLength = 253,
                    Pattern = EmailDomainPattern
                },
                Description =
                    "Ordered allowed domains for member_domain_acknowledgement_required."
            });
    }

    private static void ApplyUuid(OpenApiSchema schema, string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is { } property)
        {
            property.Format = "uuid";
        }
    }

    private static OpenApiSchema RequiredNonNullProperty(
        string propertyName,
        JsonSchemaType type,
        OpenApiSchema? items = null) =>
        new()
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(StringComparer.Ordinal)
            {
                propertyName
            },
            Properties = new Dictionary<string, IOpenApiSchema>(
                StringComparer.Ordinal)
            {
                [propertyName] = new OpenApiSchema
                {
                    Type = type,
                    Items = items
                }
            }
        };

    private static OpenApiSchema? GetPropertySchema(
        OpenApiSchema schema,
        string propertyName) =>
        schema.Properties?.TryGetValue(propertyName, out var property) == true
            ? property as OpenApiSchema
            : null;

    private static void SetPropertyStringEnum(
        OpenApiSchema schema,
        string propertyName,
        params string[] values)
    {
        if (GetPropertySchema(schema, propertyName) is not { } property)
        {
            return;
        }

        property.Type = JsonSchemaType.String;
        property.Enum =
        [
            .. values.Select(value => JsonValue.Create(value)!)
        ];
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

        if (GetPropertySchema(schema, propertyName) is { Type: { } type } property)
        {
            property.Type = type & ~JsonSchemaType.Null;
        }
    }
}
