using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Template.Api.Authentication;

namespace Template.Api.OpenApi;

internal static class OpenApiServiceCollectionExtensions
{
    internal static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiDefaults.DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            options.AddSchemaTransformer<ApiContractSchemaTransformer>();
            options.AddSchemaTransformer<OrganizationContractSchemaTransformer>();
            options.AddSchemaTransformer<CollaborationContractSchemaTransformer>();
            options.AddOperationTransformer<OrganizationContractOperationTransformer>();
            options.AddOperationTransformer<CollaborationContractOperationTransformer>();
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Template API";
                document.Info.Version = "v1";
                document.Servers?.Clear();
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[OpenApiDefaults.CookieSecurityScheme] =
                    new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Cookie,
                        Name = ApiAuthenticationDefaults.CookieName,
                        Description = "Secure HttpOnly same-origin session cookie."
                    };
                document.Components.Schemas ??=
                    new Dictionary<string, IOpenApiSchema>(
                        StringComparer.Ordinal);
                document.Components.Schemas["ExternalAuthIntent"] =
                    CreateExternalAuthIntentSchema();
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<AntiforgeryProtectedEndpointMetadata>().Any())
                {
                    operation.Parameters ??= [];
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = "X-CSRF-TOKEN",
                        In = ParameterLocation.Header,
                        Required = true,
                        Description = "Request token returned by GET /api/v1/auth/csrf.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    });
                }

                if (metadata.OfType<LocalOnlyEndpointMetadata>().Any())
                {
                    operation.Extensions ??=
                        new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                    operation.Extensions["x-local-only"] =
                        new JsonNodeExtension(JsonValue.Create(true)!);
                }

                if (metadata.OfType<ManuallyReadJsonBodyMetadata>()
                        .SingleOrDefault() is { } jsonBody &&
                    operation.RequestBody is OpenApiRequestBody requestBody &&
                    requestBody.Content?.Values.SingleOrDefault() is { } jsonMediaType)
                {
                    requestBody.Required = !jsonBody.IsOptional;
                    requestBody.Content =
                        new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
                        {
                            ["application/json"] = jsonMediaType
                        };
                }

                if (metadata.OfType<BadRequestVariantsMetadata>().Any() &&
                    operation.Responses?.TryGetValue(
                        StatusCodes.Status400BadRequest.ToString(),
                        out var response) == true &&
                    response is OpenApiResponse badRequest &&
                    badRequest.Content?.TryGetValue(
                        OpenApiDefaults.ProblemContentType,
                        out var content) == true &&
                    content is OpenApiMediaType mediaType)
                {
                    mediaType.Schema = new OpenApiSchema
                    {
                        OneOf =
                        [
                            new OpenApiSchemaReference(
                                nameof(ProblemDetails),
                                context.Document),
                            new OpenApiSchemaReference(
                                nameof(HttpValidationProblemDetails),
                                context.Document)
                        ]
                    };
                }

                ApplyAccountAndExternalAuthContract(
                    operation,
                    context.Document!);

                if (metadata.OfType<IAllowAnonymous>().Any() ||
                    !metadata.OfType<IAuthorizeData>().Any())
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(CookieSecurityRequirement(
                    context.Document!));
                return Task.CompletedTask;
            });
        });

        return services;
    }

    private static void ApplyAccountAndExternalAuthContract(
        OpenApiOperation operation,
        OpenApiDocument document)
    {
        switch (operation.OperationId)
        {
            case "ChallengeExternalAuth":
                operation.Description =
                    "The signIn intent requires an anonymous browser. The connect " +
                    "intent requires the current cookieAuth BrowserSession.";
                operation.Security =
                [
                    new OpenApiSecurityRequirement(),
                    CookieSecurityRequirement(document)
                ];
                SetParameterStringEnum(
                    operation,
                    "provider",
                    "google",
                    "github",
                    "gitlab",
                    "vk",
                    "yandex");
                AddProblemResponse(
                    operation,
                    document,
                    StatusCodes.Status409Conflict,
                    "Conflict");
                AddProblemResponse(
                    operation,
                    document,
                    StatusCodes.Status429TooManyRequests,
                    "Too Many Requests");
                break;
            case "DisconnectAccountProvider":
                SetParameterStringEnum(
                    operation,
                    "provider",
                    "google",
                    "github",
                    "gitlab",
                    "vk",
                    "yandex");
                AddProblemResponse(
                    operation,
                    document,
                    StatusCodes.Status409Conflict,
                    "Conflict");
                break;
            case "GetAccountSessions":
                ApplySessionPaginationContract(operation);
                ApplyBadRequestVariants(operation, document);
                break;
            case "RevokeAccountSession":
                AddProblemResponse(
                    operation,
                    document,
                    StatusCodes.Status409Conflict,
                    "Conflict");
                break;
        }
    }

    private static OpenApiSecurityRequirement CookieSecurityRequirement(
        OpenApiDocument document) =>
        new()
        {
            [new OpenApiSecuritySchemeReference(
                OpenApiDefaults.CookieSecurityScheme,
                document,
                null)] = []
        };

    private static OpenApiSchema CreateExternalAuthIntentSchema() =>
        new()
        {
            Type = JsonSchemaType.String,
            Enum =
            [
                JsonValue.Create("signIn")!,
                JsonValue.Create("connect")!
            ]
        };

    private static void ApplySessionPaginationContract(
        OpenApiOperation operation)
    {
        var limit = operation.Parameters?.SingleOrDefault(
            parameter => string.Equals(
                parameter.Name,
                "limit",
                StringComparison.Ordinal));
        if (limit?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Type = JsonSchemaType.Integer;
        schema.Format = "int32";
        schema.Pattern = null;
        schema.Minimum = "1";
        schema.Maximum = "100";
        schema.Default = JsonValue.Create(20);
    }

    private static void SetParameterStringEnum(
        OpenApiOperation operation,
        string parameterName,
        params string[] values)
    {
        var parameter = operation.Parameters?.SingleOrDefault(
            value => string.Equals(
                value.Name,
                parameterName,
                StringComparison.Ordinal));
        if (parameter?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Type = JsonSchemaType.String;
        schema.Enum =
        [
            .. values.Select(value => JsonValue.Create(value)!)
        ];
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

        mediaType.Schema = ProblemSchemaUnion(document);
    }

    private static void AddProblemResponse(
        OpenApiOperation operation,
        OpenApiDocument document,
        int statusCode,
        string description)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses[statusCode.ToString()] = new OpenApiResponse
        {
            Description = description,
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

    private static OpenApiSchema ProblemSchemaUnion(
        OpenApiDocument document) =>
        new()
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
