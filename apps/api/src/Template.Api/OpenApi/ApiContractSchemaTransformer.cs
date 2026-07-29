using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Contracts;
using Template.Api.Features.Account;
using Template.Api.Features.Auth;
using Template.Application.Accounts;

namespace Template.Api.OpenApi;

internal sealed class ApiContractSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly string[] ExternalProviderIds =
    [
        "google",
        "github",
        "gitlab",
        "vk",
        "yandex"
    ];

    private static readonly string[] BrowserAuthenticationMethodIds =
    [
        "local",
        .. ExternalProviderIds
    ];

    private static readonly string[] StableProblemCodes =
    [
        "invalid_request",
        "validation_failed",
        "unauthorized",
        "forbidden",
        "not_found",
        "method_not_allowed",
        "internal_error",
        "antiforgery_failed",
        "local_auth_invalid_credentials",
        "local_auth_user_required",
        "local_auth_disabled",
        "local_auth_user_exists",
        "rate_limited",
        "invalid_return_url",
        "external_provider_not_configured",
        "already_authenticated",
        "external_auth_failed",
        "external_email_required",
        "external_email_unverified",
        "external_identity_conflict",
        "external_email_conflict",
        "oauth_flow_context_changed",
        "invalid_cursor",
        "external_connection_required",
        "external_connection_not_found",
        "account_session_not_found",
        "current_session_cannot_be_revoked",
        "concurrency_conflict"
    ];

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
            if (schema.Properties["code"] is OpenApiSchema code)
            {
                SetStringEnum(code, StableProblemCodes);
            }

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

        if (type == typeof(ExternalAuthIntent) ||
            Nullable.GetUnderlyingType(type) == typeof(ExternalAuthIntent))
        {
            SetStringEnum(schema, "signIn", "connect");
        }

        if (type == typeof(ExternalAuthChallengeRequest))
        {
            ApplyExternalChallengeRequestContract(schema);
        }

        if (type == typeof(ExternalAuthChallengeResponse))
        {
            ApplyHttpsUriProjection(schema, "authorizationUrl");
        }

        if (type == typeof(UpdateProfileRequest))
        {
            ApplyUpdateProfileContract(schema);
        }

        if (type == typeof(DeleteAccountRequest))
        {
            ApplyDeleteAccountContract(schema);
        }

        if (type == typeof(AuthProviderResponse))
        {
            SetPropertyStringEnum(schema, "id", ExternalProviderIds);
        }

        if (type == typeof(AccountEmailResponse))
        {
            SetArrayItemStringEnum(schema, "providers", ExternalProviderIds);
            ApplyEmailProjection(schema, "email");
        }

        if (type == typeof(AccountResponse))
        {
            ApplyEmailProjection(schema, "primaryEmail");
            ApplyHttpsUriProjection(schema, "imageUrl");
            if (GetPropertySchema(schema, "displayName") is { } displayName)
            {
                displayName.MinLength = 2;
                displayName.MaxLength = 50;
            }
        }

        if (type == typeof(AccountConnectionResponse))
        {
            SetPropertyStringEnum(schema, "provider", ExternalProviderIds);
            ApplyEmailProjection(schema, "email");
            SetPropertyStringEnum(
                schema,
                "disabledReason",
                nullable: true,
                "external_connection_required");
        }

        if (type == typeof(AccountDisconnectionResponse))
        {
            SetPropertyStringEnum(schema, "provider", ExternalProviderIds);
        }

        if (type == typeof(AccountSessionResponse))
        {
            SetPropertyStringEnum(
                schema,
                "authenticationMethod",
                BrowserAuthenticationMethodIds);
            if (GetPropertySchema(schema, "userAgent") is { } userAgent)
            {
                userAgent.MaxLength = 512;
            }
        }

        if (type == typeof(AccountSessionsRevocationResponse) &&
            GetPropertySchema(schema, "revokedCount") is { } revokedCount)
        {
            revokedCount.Type = JsonSchemaType.Integer;
            revokedCount.Format = "int32";
            revokedCount.Pattern = null;
            revokedCount.Minimum = "0";
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

    private static void ApplyExternalChallengeRequestContract(OpenApiSchema schema)
    {
        schema.Required?.Remove("returnUrl");
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        schema.Required.Add("intent");
        if (schema.Properties is not null)
        {
            var intent = new OpenApiSchema();
            SetStringEnum(intent, "signIn", "connect");
            schema.Properties["intent"] = intent;
        }

        if (GetPropertySchema(schema, "returnUrl") is { } returnUrl)
        {
            returnUrl.MaxLength = 4096;
            returnUrl.Pattern = "^/(?!/)";
            AddExtension(returnUrl, "x-safe-return-path", true);
            returnUrl.Description =
                "Optional same-origin application path. Absolute, protocol-relative, " +
                "/api/**, and /auth/** return paths are rejected.";
        }
    }

    private static void ApplyUpdateProfileContract(OpenApiSchema schema)
    {
        MakePropertyRequiredAndNonNull(schema, "displayName");
        if (GetPropertySchema(schema, "displayName") is not { } displayName)
        {
            return;
        }

        AddExtension(displayName, "x-trimmed-min-length", 2);
        AddExtension(displayName, "x-trimmed-max-length", 50);
        displayName.Description =
            "Trimmed before use; the trimmed display name must contain 2 to 50 " +
            "characters and must not contain control characters.";
    }

    private static void ApplyDeleteAccountContract(OpenApiSchema schema)
    {
        MakePropertyRequiredAndNonNull(schema, "confirmationEmail");
        if (GetPropertySchema(schema, "confirmationEmail") is not { } email)
        {
            return;
        }

        AddExtension(email, "x-trimmed-max-length", 254);
        AddExtension(email, "x-trimmed-format", "email");
        email.Description =
            "Trimmed before comparison; the value must be a valid email of at most " +
            "254 characters and exactly match the current primary email.";
    }

    private static void ApplyEmailProjection(
        OpenApiSchema schema,
        string propertyName)
    {
        if (GetPropertySchema(schema, propertyName) is not { } email)
        {
            return;
        }

        email.Format = "email";
        email.MaxLength = 254;
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

    private static OpenApiSchema? GetPropertySchema(
        OpenApiSchema schema,
        string propertyName) =>
        schema.Properties?.TryGetValue(propertyName, out var property) == true
            ? property as OpenApiSchema
            : null;

    private static void SetPropertyStringEnum(
        OpenApiSchema schema,
        string propertyName,
        params string[] values) =>
        SetPropertyStringEnum(schema, propertyName, nullable: false, values);

    private static void SetPropertyStringEnum(
        OpenApiSchema schema,
        string propertyName,
        bool nullable,
        params string[] values)
    {
        if (GetPropertySchema(schema, propertyName) is { } property)
        {
            SetStringEnum(property, nullable, values);
        }
    }

    private static void SetArrayItemStringEnum(
        OpenApiSchema schema,
        string propertyName,
        params string[] values)
    {
        if (GetPropertySchema(schema, propertyName)?.Items is OpenApiSchema items)
        {
            SetStringEnum(items, values);
        }
    }

    private static void SetStringEnum(
        OpenApiSchema schema,
        params string[] values) =>
        SetStringEnum(schema, nullable: false, values);

    private static void SetStringEnum(
        OpenApiSchema schema,
        bool nullable,
        params string[] values)
    {
        schema.Type = nullable
            ? JsonSchemaType.String | JsonSchemaType.Null
            : JsonSchemaType.String;
        var enumValues = values
            .Select(value => JsonValue.Create(value)!)
            .Cast<JsonNode>()
            .ToList();
        if (nullable)
        {
            enumValues.Add(JsonNullSentinel.JsonNull);
        }

        schema.Enum = enumValues;
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
