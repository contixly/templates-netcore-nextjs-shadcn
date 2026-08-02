using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using Template.Api.Authentication;
using Template.Api.Features.ApiKeys;
using Template.Domain.ApiKeys;

namespace Template.Api.OpenApi;

internal sealed class ApiKeyContractOperationTransformer
    : IOpenApiOperationTransformer
{
    private const string CanonicalUuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-" +
        "[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$";
    private const string ManagementCursorDescription =
        "Opaque, typed, versioned, checksum-protected canonical base64url cursor " +
        "for (createdAt DESC, apiKeyId DESC). Return nextCursor verbatim; do not " +
        "decode or synthesize it.";

    private static readonly IReadOnlyDictionary<string, Contract> Contracts =
        new Dictionary<string, Contract>(StringComparer.Ordinal)
        {
            ["ListPersonalApiKeys"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 405, 500],
                BadRequestKind.Validation,
                Scopes: null,
                Pagination: true,
                Location: false,
                RevealOnce: false),
            ["CreatePersonalApiKey"] = new(
                SecurityKind.Cookie,
                201,
                [400, 401, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: true,
                RevealOnce: true),
            ["UpdatePersonalApiKey"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["RevokePersonalApiKey"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["RotatePersonalApiKey"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: false,
                RevealOnce: true),
            ["ListOrganizationApiKeys"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 403, 404, 405, 409, 500],
                BadRequestKind.Validation,
                Scopes: null,
                Pagination: true,
                Location: false,
                RevealOnce: false),
            ["CreateOrganizationApiKey"] = new(
                SecurityKind.Cookie,
                201,
                [400, 401, 403, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: true,
                RevealOnce: true),
            ["UpdateOrganizationApiKey"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 403, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["RevokeOrganizationApiKey"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 403, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["RotateOrganizationApiKey"] = new(
                SecurityKind.Cookie,
                200,
                [400, 401, 403, 404, 405, 409, 500],
                BadRequestKind.Union,
                Scopes: null,
                Pagination: false,
                Location: false,
                RevealOnce: true),
            ["GetApiKeyPrincipal"] = new(
                SecurityKind.ApiKey,
                200,
                [401, 403, 405, 429, 500],
                BadRequestKind.None,
                ["basic:read"],
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["GetOrganizations"] = new(
                SecurityKind.CookieOrApiKey,
                200,
                [400, 401, 403, 405, 429, 500],
                BadRequestKind.Union,
                ["organization:read"],
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["GetMachineOrganization"] = new(
                SecurityKind.ApiKey,
                200,
                [400, 401, 403, 404, 405, 429, 500],
                BadRequestKind.Validation,
                ["organization:read"],
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["GetOrganizationMembers"] = new(
                SecurityKind.CookieOrApiKey,
                200,
                [400, 401, 403, 404, 405, 409, 429, 500],
                BadRequestKind.Union,
                ["organization:read", "member:read"],
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["GetTeams"] = new(
                SecurityKind.CookieOrApiKey,
                200,
                [400, 401, 403, 404, 405, 409, 429, 500],
                BadRequestKind.Union,
                ["organization:read", "team:read"],
                Pagination: false,
                Location: false,
                RevealOnce: false),
            ["GetTeamMembers"] = new(
                SecurityKind.CookieOrApiKey,
                200,
                [400, 401, 403, 404, 405, 409, 429, 500],
                BadRequestKind.Union,
                ["organization:read", "team:read", "teamMember:read"],
                Pagination: false,
                Location: false,
                RevealOnce: false)
        };

    private static readonly IReadOnlyDictionary<string, EndpointContract>
        EndpointContracts =
            new Dictionary<string, EndpointContract>(StringComparer.Ordinal)
            {
                ["ListPersonalApiKeys"] = new(
                    "GET", "/api/v1/account/api-keys",
                    ApiKeyOwnerKind.User, Antiforgery: false,
                    SynthesizedPathParameter: null),
                ["CreatePersonalApiKey"] = new(
                    "POST", "/api/v1/account/api-keys",
                    ApiKeyOwnerKind.User, Antiforgery: true,
                    SynthesizedPathParameter: null),
                ["UpdatePersonalApiKey"] = new(
                    "PATCH", "/api/v1/account/api-keys/{apiKeyId}",
                    ApiKeyOwnerKind.User, Antiforgery: true,
                    SynthesizedPathParameter: null),
                ["RevokePersonalApiKey"] = new(
                    "DELETE", "/api/v1/account/api-keys/{apiKeyId}",
                    ApiKeyOwnerKind.User, Antiforgery: true,
                    SynthesizedPathParameter: null),
                ["RotatePersonalApiKey"] = new(
                    "POST", "/api/v1/account/api-keys/{apiKeyId}/rotate",
                    ApiKeyOwnerKind.User, Antiforgery: true,
                    SynthesizedPathParameter: null),
                ["ListOrganizationApiKeys"] = new(
                    "GET", "/api/v1/organizations/{organizationId}/api-keys",
                    ApiKeyOwnerKind.Organization, Antiforgery: false,
                    SynthesizedPathParameter: "organizationId"),
                ["CreateOrganizationApiKey"] = new(
                    "POST", "/api/v1/organizations/{organizationId}/api-keys",
                    ApiKeyOwnerKind.Organization, Antiforgery: true,
                    SynthesizedPathParameter: "organizationId"),
                ["UpdateOrganizationApiKey"] = new(
                    "PATCH",
                    "/api/v1/organizations/{organizationId}/api-keys/{apiKeyId}",
                    ApiKeyOwnerKind.Organization, Antiforgery: true,
                    SynthesizedPathParameter: "organizationId"),
                ["RevokeOrganizationApiKey"] = new(
                    "DELETE",
                    "/api/v1/organizations/{organizationId}/api-keys/{apiKeyId}",
                    ApiKeyOwnerKind.Organization, Antiforgery: true,
                    SynthesizedPathParameter: "organizationId"),
                ["RotateOrganizationApiKey"] = new(
                    "POST",
                    "/api/v1/organizations/{organizationId}/api-keys/{apiKeyId}/rotate",
                    ApiKeyOwnerKind.Organization, Antiforgery: true,
                    SynthesizedPathParameter: "organizationId"),
                ["GetApiKeyPrincipal"] = new(
                    "GET", "/api/v1/me", Owner: null, Antiforgery: false,
                    SynthesizedPathParameter: null),
                ["GetOrganizations"] = new(
                    "GET", "/api/v1/organizations", Owner: null,
                    Antiforgery: false, SynthesizedPathParameter: null),
                ["GetMachineOrganization"] = new(
                    "GET", "/api/v1/organizations/{organizationId}", Owner: null,
                    Antiforgery: false, SynthesizedPathParameter: null),
                ["GetOrganizationMembers"] = new(
                    "GET", "/api/v1/organizations/{organizationId}/members",
                    Owner: null, Antiforgery: false,
                    SynthesizedPathParameter: null),
                ["GetTeams"] = new(
                    "GET", "/api/v1/organizations/{organizationId}/teams",
                    Owner: null, Antiforgery: false,
                    SynthesizedPathParameter: null),
                ["GetTeamMembers"] = new(
                    "GET",
                    "/api/v1/organizations/{organizationId}/teams/{teamId}/members",
                    Owner: null, Antiforgery: false,
                    SynthesizedPathParameter: null)
            };

    static ApiKeyContractOperationTransformer()
    {
        if (!Contracts.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                EndpointContracts.Keys.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "API key OpenAPI contract and endpoint tables must have identical operation names.");
        }
    }

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var hasApiKeyMetadata = metadata.OfType<ApiKeyScopeMetadata>().Any() ||
            metadata.OfType<ApiKeyEndpointModule.ApiKeyOwnerRouteMetadata>().Any();
        if (operation.OperationId is null ||
            !Contracts.TryGetValue(operation.OperationId, out var contract))
        {
            if (hasApiKeyMetadata)
            {
                throw new InvalidOperationException(
                    $"Scoped or owner-bound API key operation " +
                    $"{operation.OperationId ?? "<unnamed>"} is not in the bounded contract table.");
            }

            return Task.CompletedTask;
        }

        var endpointContract = EndpointContracts[operation.OperationId];
        ValidateScopeMetadata(operation, context, contract);
        ValidateEndpointMetadata(operation, context, contract, endpointContract);
        ApplyExactResponses(operation, context.Document!, contract);
        ApplySecurity(operation, context.Document!, contract.Security);
        ApplyPathParameterContract(operation, endpointContract);
        ApplyNoStore(operation);

        if (contract.Scopes is not null)
        {
            ApplyScopes(operation, contract.Scopes);
            ApplyRetryAfter(operation);
        }

        if (contract.Pagination)
        {
            ApplyManagementPagination(operation);
        }

        if (contract.Location)
        {
            ApplyLocation(operation);
        }

        if (contract.RevealOnce)
        {
            operation.Description =
                "Returns the raw API key exactly once. Store it securely before " +
                "discarding this response; it cannot be retrieved later.";
        }

        return Task.CompletedTask;
    }

    private static void ValidateEndpointMetadata(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        Contract contract,
        EndpointContract endpointContract)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var declaredMethods = metadata.OfType<IHttpMethodMetadata>()
            .SelectMany(value => value.HttpMethods)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (declaredMethods.Length != 1 ||
            !string.Equals(
                declaredMethods[0],
                endpointContract.Method,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} HTTP method metadata drifted.");
        }

        var expectedPolicy = contract.Security switch
        {
            SecurityKind.Cookie => ApiPolicies.BrowserSession,
            SecurityKind.ApiKey => ApiPolicies.MachineKey,
            SecurityKind.CookieOrApiKey => ApiPolicies.BrowserOrMachine,
            _ => throw new ArgumentOutOfRangeException(nameof(contract))
        };
        var authorizationData = metadata.OfType<IAuthorizeData>()
            .ToArray();
        if (authorizationData.Length != 1 ||
            !string.Equals(
                authorizationData[0].Policy,
                expectedPolicy,
                StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(authorizationData[0].Roles) ||
            !string.IsNullOrEmpty(authorizationData[0].AuthenticationSchemes))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} authorization policy metadata drifted.");
        }

        var owners = metadata
            .OfType<ApiKeyEndpointModule.ApiKeyOwnerRouteMetadata>()
            .ToArray();
        if (endpointContract.Owner is { } expectedOwner)
        {
            if (owners.Length != 1 || owners[0].Kind != expectedOwner)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.OperationId} owner metadata drifted.");
            }
        }
        else if (owners.Length != 0)
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} unexpectedly has owner metadata.");
        }

        var antiforgeryCount = metadata
            .OfType<AntiforgeryProtectedEndpointMetadata>()
            .Count();
        if (antiforgeryCount != (endpointContract.Antiforgery ? 1 : 0))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} antiforgery metadata drifted.");
        }

        if (!string.Equals(
                context.Description.HttpMethod,
                endpointContract.Method,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} described HTTP method drifted.");
        }

        var relativePath = context.Description.RelativePath;
        var actualPath = relativePath is null
            ? null
            : NormalizeRoutePath(relativePath);
        if (!string.Equals(
                actualPath,
                endpointContract.Path,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} route template drifted: " +
                $"expected {endpointContract.Path}, actual {actualPath ?? "<missing>"}.");
        }
    }

    private static string NormalizeRoutePath(string path) =>
        "/" + path.Trim('/');

    private static void ValidateScopeMetadata(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        Contract contract)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ApiKeyScopeMetadata>()
            .SingleOrDefault();
        if (contract.Scopes is null)
        {
            if (metadata is not null)
            {
                throw new InvalidOperationException(
                    $"Management operation {operation.OperationId} unexpectedly has API key scope metadata.");
            }

            return;
        }

        if (metadata is null ||
            !metadata.Scopes.SequenceEqual(contract.Scopes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} API key scope metadata drifted.");
        }
    }

    private static void ApplyExactResponses(
        OpenApiOperation operation,
        OpenApiDocument document,
        Contract contract)
    {
        operation.Responses ??= new OpenApiResponses();
        var expected = contract.Problems
            .Append(contract.Success)
            .Select(status => status.ToString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var status in operation.Responses.Keys
                     .Where(status => int.TryParse(status, out _) &&
                         !expected.Contains(status))
                     .ToArray())
        {
            operation.Responses.Remove(status);
        }

        if (!operation.Responses.ContainsKey(contract.Success.ToString()))
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} is missing success status {contract.Success}.");
        }

        foreach (var status in contract.Problems)
        {
            operation.Responses[status.ToString()] = new OpenApiResponse
            {
                Description = ReasonPhrase(status),
                Content = new Dictionary<string, OpenApiMediaType>(
                    StringComparer.Ordinal)
                {
                    [OpenApiDefaults.ProblemContentType] = new()
                    {
                        Schema = ProblemSchema(document, status, contract.BadRequest)
                    }
                }
            };
        }
    }

    private static IOpenApiSchema ProblemSchema(
        OpenApiDocument document,
        int status,
        BadRequestKind badRequest) =>
        status == StatusCodes.Status400BadRequest
            ? badRequest switch
            {
                BadRequestKind.Validation => new OpenApiSchemaReference(
                    nameof(HttpValidationProblemDetails),
                    document),
                BadRequestKind.Union => new OpenApiSchema
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
                },
                _ => throw new InvalidOperationException(
                    "A 400 response requires an exact schema contract.")
            }
            : new OpenApiSchemaReference(nameof(ProblemDetails), document);

    private static void ApplySecurity(
        OpenApiOperation operation,
        OpenApiDocument document,
        SecurityKind security)
    {
        operation.Security = security switch
        {
            SecurityKind.Cookie => [SecurityRequirement(
                document,
                OpenApiDefaults.CookieSecurityScheme)],
            SecurityKind.ApiKey => [SecurityRequirement(
                document,
                OpenApiDefaults.ApiKeySecurityScheme)],
            SecurityKind.CookieOrApiKey =>
            [
                SecurityRequirement(
                    document,
                    OpenApiDefaults.CookieSecurityScheme),
                SecurityRequirement(
                    document,
                    OpenApiDefaults.ApiKeySecurityScheme)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(security))
        };
    }

    private static OpenApiSecurityRequirement SecurityRequirement(
        OpenApiDocument document,
        string scheme) =>
        new()
        {
            [new OpenApiSecuritySchemeReference(scheme, document, null)] = []
        };

    private static void ApplyScopes(
        OpenApiOperation operation,
        IReadOnlyList<string> scopes)
    {
        var values = new JsonArray();
        foreach (var scope in scopes)
        {
            values.Add(scope);
        }

        operation.Extensions ??=
            new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions["x-api-key-scopes"] = new JsonNodeExtension(values);
    }

    private static void ApplyNoStore(OpenApiOperation operation)
    {
        operation.Extensions ??=
            new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions["x-cache-control"] =
            new JsonNodeExtension(JsonValue.Create("no-store")!);
        foreach (var response in operation.Responses?.Values.OfType<OpenApiResponse>() ?? [])
        {
            response.Headers ??=
                new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
            response.Headers["Cache-Control"] = new OpenApiHeader
            {
                Required = true,
                Description = "All responses are non-cacheable.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("no-store")!]
                }
            };
        }
    }

    private static void ApplyRetryAfter(OpenApiOperation operation)
    {
        if (operation.Responses?.TryGetValue("429", out var response) != true ||
            response is not OpenApiResponse rateLimited)
        {
            throw new InvalidOperationException(
                $"Machine operation {operation.OperationId} is missing 429.");
        }

        rateLimited.Headers ??=
            new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        rateLimited.Headers["Retry-After"] = new OpenApiHeader
        {
            Required = true,
            Description =
                "Whole seconds until this API key's fixed rate-limit window permits another request.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int32",
                Minimum = "1",
                Maximum = "86400"
            }
        };
    }

    private static void ApplyManagementPagination(OpenApiOperation operation)
    {
        var limit = RequiredParameterSchema(operation, "limit");
        limit.Type = JsonSchemaType.Integer;
        limit.Format = "int32";
        limit.Pattern = null;
        limit.Minimum = "1";
        limit.Maximum = "100";
        limit.Default = JsonValue.Create(50);

        var cursor = RequiredParameterSchema(operation, "cursor");
        cursor.Type = JsonSchemaType.String;
        cursor.Format = null;
        cursor.Description = ManagementCursorDescription;
    }

    private static void ApplyPathParameterContract(
        OpenApiOperation operation,
        EndpointContract contract)
    {
        var expectedNames = PathParameterNames(contract.Path);
        foreach (var parameterName in expectedNames)
        {
            var matches = operation.Parameters?
                .Where(parameter =>
                    parameter.In == ParameterLocation.Path &&
                    string.Equals(
                        parameter.Name,
                        parameterName,
                        StringComparison.Ordinal))
                .ToArray() ?? [];
            IOpenApiParameter parameter;
            if (matches.Length == 0 &&
                string.Equals(
                    parameterName,
                    contract.SynthesizedPathParameter,
                    StringComparison.Ordinal))
            {
                parameter = new OpenApiParameter
                {
                    Name = parameterName,
                    In = ParameterLocation.Path,
                    Required = true,
                    Schema = new OpenApiSchema()
                };
                operation.Parameters ??= [];
                operation.Parameters.Add(parameter);
            }
            else if (matches.Length == 1)
            {
                parameter = matches[0];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Operation {operation.OperationId} requires exactly one " +
                    $"{parameterName} path parameter before contract transformation.");
            }

            if (parameter.Required != true ||
                parameter.Schema is not OpenApiSchema schema)
            {
                throw new InvalidOperationException(
                    $"Operation {operation.OperationId} path parameter " +
                    $"{parameterName} must be required and have an inline schema.");
            }

            schema.Type = JsonSchemaType.String;
            schema.Format = "uuid";
            schema.Pattern = CanonicalUuidPattern;
        }

        var extras = operation.Parameters?
            .Where(parameter => parameter.In == ParameterLocation.Path)
            .Select(parameter => parameter.Name)
            .Where(name => !expectedNames.Contains(name, StringComparer.Ordinal))
            .ToArray() ?? [];
        if (extras.Length != 0)
        {
            throw new InvalidOperationException(
                $"Operation {operation.OperationId} has undeclared path parameters: " +
                string.Join(", ", extras));
        }
    }

    private static IReadOnlyList<string> PathParameterNames(string path)
    {
        var names = new List<string>();
        for (var start = path.IndexOf('{');
             start >= 0;
             start = path.IndexOf('{', start))
        {
            var end = path.IndexOf('}', start + 1);
            if (end < 0)
            {
                throw new InvalidOperationException(
                    $"Invalid API key route template {path}.");
            }

            names.Add(path[(start + 1)..end]);
            start = end + 1;
        }

        if (names.Count != names.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidOperationException(
                $"Duplicate API key route parameter in {path}.");
        }

        return names;
    }

    private static OpenApiSchema RequiredParameterSchema(
        OpenApiOperation operation,
        string name) =>
        Parameter(operation, name)?.Schema as OpenApiSchema
        ?? throw new InvalidOperationException(
            $"Operation {operation.OperationId} is missing the {name} parameter schema.");

    private static IOpenApiParameter? Parameter(
        OpenApiOperation operation,
        string name) =>
        operation.Parameters?.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

    private static void ApplyLocation(OpenApiOperation operation)
    {
        if (operation.Responses?.TryGetValue("201", out var response) != true ||
            response is not OpenApiResponse created)
        {
            throw new InvalidOperationException(
                $"Create operation {operation.OperationId} is missing 201.");
        }

        created.Headers ??=
            new Dictionary<string, IOpenApiHeader>(StringComparer.Ordinal);
        created.Headers["Location"] = new OpenApiHeader
        {
            Required = true,
            Description = "URI reference for the created API key resource.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "uri-reference"
            }
        };
    }

    private static string ReasonPhrase(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        StatusCodes.Status500InternalServerError => "Internal Server Error",
        _ => throw new InvalidOperationException(
            $"Unsupported API key problem status {status}.")
    };

    private sealed record Contract(
        SecurityKind Security,
        int Success,
        int[] Problems,
        BadRequestKind BadRequest,
        IReadOnlyList<string>? Scopes,
        bool Pagination,
        bool Location,
        bool RevealOnce);

    private sealed record EndpointContract(
        string Method,
        string Path,
        ApiKeyOwnerKind? Owner,
        bool Antiforgery,
        string? SynthesizedPathParameter);

    private enum SecurityKind
    {
        Cookie,
        ApiKey,
        CookieOrApiKey
    }

    private enum BadRequestKind
    {
        None,
        Validation,
        Union
    }
}
