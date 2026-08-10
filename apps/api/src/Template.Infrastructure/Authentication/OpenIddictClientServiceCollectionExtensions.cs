using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Client.WebIntegration;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Infrastructure.Authentication;

public static class OpenIddictClientServiceCollectionExtensions
{
    public static IServiceCollection AddOpenIddictExternalClient(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<ExternalAuthenticationOptions>()
            .Bind(configuration.GetSection(
                ExternalAuthenticationOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IValidateOptions<ExternalAuthenticationOptions>,
                ExternalAuthenticationOptionsValidator>());
        services.TryAddSingleton<IExternalProviderCatalog, ExternalProviderCatalog>();
        services.TryAddScoped<
            IExternalIdentityNormalizer,
            ExternalIdentityNormalizer>();
        services
            .AddHttpClient<IExternalUserInfoClient, GitHubExternalUserInfoClient>(
                client =>
                {
                    client.BaseAddress = new Uri(
                        "https://api.github.com/",
                        UriKind.Absolute);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Template.Api/1.0");
                    client.Timeout = TimeSpan.FromSeconds(10);
                });

        var configured = configuration
            .GetSection(ExternalAuthenticationOptions.SectionName)
            .Get<ExternalAuthenticationOptions>()
            ?? new ExternalAuthenticationOptions();

        var openIddict = services
            .AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<TemplateDbContext>();
            });

        openIddict.AddClient(options =>
        {
            options.AllowAuthorizationCodeFlow();
            options.SetRedirectionEndpointUris(
                ExternalProviderMetadata.All
                    .Select(ExternalProviderMetadata.GetCallbackPath)
                    .ToArray());
            options.AddEphemeralEncryptionKey()
                .AddEphemeralSigningKey();
            options.UseDataProtection();
            options.AddEventHandler<
                OpenIddictClientEvents.ApplyRedirectionResponseContext>(
                builder =>
                builder
                    .UseInlineHandler(context =>
                    {
                        var request = context.Transaction.GetHttpRequest();
                        if (request is not null
                            && !string.IsNullOrEmpty(context.Response.Error)
                            && ExternalProviderMetadata.All.Any(provider =>
                                string.Equals(
                                    request.Path,
                                    ExternalProviderMetadata.GetCallbackPath(
                                        provider),
                                    StringComparison.Ordinal)))
                        {
                            // OpenIddict attaches a 400 status before passing
                            // redirection errors through to the application.
                            // Reset only that status so the callback endpoint
                            // can replace it with the stable browser redirect.
                            request.HttpContext.Response.StatusCode =
                                StatusCodes.Status200OK;
                        }

                        return default;
                    })
                    .SetOrder(
                        OpenIddictClientAspNetCoreHandlers
                            .AttachHttpResponseCode<
                                OpenIddictClientEvents
                                    .ApplyRedirectionResponseContext>
                            .Descriptor.Order + 500));

            var aspNetCore = options.UseAspNetCore()
                .EnableRedirectionEndpointPassthrough()
                .EnableErrorPassthrough();
            if (configured.TryGetPublicOrigin(out var publicOrigin)
                && publicOrigin!.Scheme == Uri.UriSchemeHttp)
            {
                aspNetCore.DisableTransportSecurityRequirement();
            }

            options.UseSystemNetHttp()
                .SetProductInformation(
                    typeof(OpenIddictClientServiceCollectionExtensions)
                        .Assembly);

            if (publicOrigin is null)
            {
                return;
            }

            var providers = options.UseWebProviders();
            AddGoogle(
                providers,
                configured,
                publicOrigin,
                environment.IsProduction());
            AddGitHub(providers, configured, publicOrigin);
            AddGitLab(providers, configured, publicOrigin);
            AddVk(providers, configured, publicOrigin);
            AddYandex(options, configured, publicOrigin);
        });

        services.TryAddSingleton<OpenIddictStateCleanupService>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<OpenIddictStateCleanupService>());
        return services;
    }

    private static void AddGoogle(
        OpenIddictClientWebIntegrationBuilder providers,
        ExternalAuthenticationOptions options,
        Uri publicOrigin,
        bool isProduction)
    {
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.Google,
                out var credentials))
        {
            return;
        }

        providers.AddGoogle(registration =>
        {
            registration
                .SetRegistrationId("google")
                .SetProviderName("google")
                .SetProviderDisplayName("Google")
                .SetClientId(credentials!.ClientId!)
                .SetClientSecret(credentials.ClientSecret!)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.Google))
                .AddScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email);
            if (isProduction)
            {
                registration.SetPrompt("select_account");
            }
        });
    }

    private static void AddGitHub(
        OpenIddictClientWebIntegrationBuilder providers,
        ExternalAuthenticationOptions options,
        Uri publicOrigin)
    {
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.GitHub,
                out var credentials))
        {
            return;
        }

        providers.AddGitHub(registration =>
            registration
                .SetRegistrationId("github")
                .SetProviderName("github")
                .SetProviderDisplayName("GitHub")
                .SetClientId(credentials!.ClientId!)
                .SetClientSecret(credentials.ClientSecret!)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.GitHub))
                .AddScopes("user:email"));
    }

    private static void AddGitLab(
        OpenIddictClientWebIntegrationBuilder providers,
        ExternalAuthenticationOptions options,
        Uri publicOrigin)
    {
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.GitLab,
                out var credentials))
        {
            return;
        }

        providers.AddGitLab(registration =>
            registration
                .SetRegistrationId("gitlab")
                .SetProviderName("gitlab")
                .SetProviderDisplayName("GitLab")
                .SetClientId(credentials!.ClientId!)
                .SetClientSecret(credentials.ClientSecret!)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.GitLab))
                .AddScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email));
    }

    private static void AddVk(
        OpenIddictClientWebIntegrationBuilder providers,
        ExternalAuthenticationOptions options,
        Uri publicOrigin)
    {
        if (!options.TryGetClientId(
                Template.Domain.Accounts.ExternalProvider.Vk,
                out var clientId))
        {
            return;
        }

        providers.AddVkId(registration =>
            registration
                .SetRegistrationId("vk")
                .SetProviderName("vk")
                .SetProviderDisplayName("VK")
                .SetClientId(clientId!)
                .SetClientType(ClientTypes.Public)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.Vk))
                .AddScopes(Scopes.Email));
    }

    private static void AddYandex(
        OpenIddictClientBuilder client,
        ExternalAuthenticationOptions options,
        Uri publicOrigin)
    {
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.Yandex,
                out var credentials))
        {
            return;
        }

        YandexOpenIddictClientIntegration.Add(
            client,
            credentials!.ClientId!,
            credentials.ClientSecret!,
            CallbackUri(
                publicOrigin,
                Template.Domain.Accounts.ExternalProvider.Yandex));
    }

    private static Uri CallbackUri(
        Uri publicOrigin,
        Template.Domain.Accounts.ExternalProvider provider) =>
        new(
            publicOrigin,
            ExternalProviderMetadata.GetCallbackPath(provider));
}
