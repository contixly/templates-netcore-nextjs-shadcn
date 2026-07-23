using Microsoft.AspNetCore.Authorization;
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
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAllowAnonymous>().Any() ||
                    !metadata.OfType<IAuthorizeData>().Any())
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(
                        OpenApiDefaults.CookieSecurityScheme,
                        context.Document,
                        null)] = []
                });
                return Task.CompletedTask;
            });
        });

        return services;
    }
}
