using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Client.WebIntegration;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Infrastructure.Authentication;

public static class OpenIddictClientServiceCollectionExtensions
{
    public static IServiceCollection AddOpenIddictExternalClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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
                    .UseDbContext<AuthDbContext>();
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

            var aspNetCore = options.UseAspNetCore()
                .EnableRedirectionEndpointPassthrough();
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
            AddGoogle(providers, configured, publicOrigin);
            AddGitHub(providers, configured, publicOrigin);
            AddGitLab(providers, configured, publicOrigin);
            AddVk(providers, configured, publicOrigin);
            AddYandex(providers, configured, publicOrigin);
        });

        services.TryAddSingleton<OpenIddictStateCleanupService>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<OpenIddictStateCleanupService>());
        return services;
    }

    private static void AddGoogle(
        OpenIddictClientWebIntegrationBuilder providers,
        ExternalAuthenticationOptions options,
        Uri publicOrigin)
    {
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.Google,
                out var credentials))
        {
            return;
        }

        providers.AddGoogle(registration =>
            registration
                .SetRegistrationId("google")
                .SetProviderName("google")
                .SetProviderDisplayName("Google")
                .SetClientId(credentials!.ClientId!)
                .SetClientSecret(credentials.ClientSecret!)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.Google))
                .AddScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email));
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
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.Vk,
                out var credentials))
        {
            return;
        }

        providers.AddVkId(registration =>
            registration
                .SetRegistrationId("vk")
                .SetProviderName("vk")
                .SetProviderDisplayName("VK")
                .SetClientId(credentials!.ClientId!)
                .SetClientSecret(credentials.ClientSecret!)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.Vk))
                .AddScopes(Scopes.Email));
    }

    private static void AddYandex(
        OpenIddictClientWebIntegrationBuilder providers,
        ExternalAuthenticationOptions options,
        Uri publicOrigin)
    {
        if (!options.TryGetCompleteCredentials(
                Template.Domain.Accounts.ExternalProvider.Yandex,
                out var credentials))
        {
            return;
        }

        providers.AddYandex(registration =>
            registration
                .SetRegistrationId("yandex")
                .SetProviderName("yandex")
                .SetProviderDisplayName("Yandex")
                .SetClientId(credentials!.ClientId!)
                .SetClientSecret(credentials.ClientSecret!)
                .SetRedirectUri(CallbackUri(
                    publicOrigin,
                    Template.Domain.Accounts.ExternalProvider.Yandex))
                .AddScopes("login:email", "login:info", "login:avatar"));
    }

    private static Uri CallbackUri(
        Uri publicOrigin,
        Template.Domain.Accounts.ExternalProvider provider) =>
        new(
            publicOrigin,
            ExternalProviderMetadata.GetCallbackPath(provider));
}
