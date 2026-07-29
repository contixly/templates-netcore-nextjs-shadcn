using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using OpenIddict.Client.DataProtection;
using OpenIddict.Client.WebIntegration;
using Template.Domain.Accounts;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Api.Tests.Accounts;

public sealed class ExternalProviderConfigurationTests
{
    private static readonly IReadOnlyDictionary<string, string> CallbackPaths =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["google"] = "/api/auth/callback/google",
            ["github"] = "/api/auth/callback/github",
            ["gitlab"] = "/api/auth/callback/gitlab",
            ["vk"] = "/api/auth/callback/vk",
            ["yandex"] = "/api/auth/oauth2/callback/yandex"
        };

    [Fact]
    public void AbsentProviderBlocksLeaveFiveKnownProvidersUnconfigured()
    {
        using var services = BuildServices([]);
        var catalog = services.GetRequiredService<IExternalProviderCatalog>();
        var registrations = services
            .GetRequiredService<IOptions<OpenIddictClientOptions>>()
            .Value
            .Registrations;

        Assert.Equal(
            ["google", "github", "gitlab", "vk", "yandex"],
            catalog.Known.Select(provider => provider.Provider.Value));
        Assert.All(catalog.Known, provider => Assert.False(provider.Configured));
        Assert.Empty(registrations);
    }

    [Fact]
    public void CompleteProviderBlocksRegisterExactCallbacksScopesAndSchemes()
    {
        using var services = BuildServices(CompleteConfiguration());
        var catalog = services.GetRequiredService<IExternalProviderCatalog>();
        var options = services
            .GetRequiredService<IOptions<OpenIddictClientOptions>>()
            .Value;

        Assert.Equal([GrantTypes.AuthorizationCode], options.GrantTypes);
        Assert.False(options.DisableTokenStorage);
        Assert.Equal(5, options.Registrations.Count);
        Assert.All(catalog.Known, provider =>
        {
            Assert.True(provider.Configured);
            Assert.Equal(
                provider.Provider.Value,
                catalog.GetAuthenticationScheme(provider.Provider));
        });

        var expectedScopes = new Dictionary<string, string[]>(
            StringComparer.Ordinal)
        {
            ["google"] = [Scopes.Email, Scopes.OpenId, Scopes.Profile],
            ["github"] = ["user:email"],
            ["gitlab"] = [Scopes.Email, Scopes.OpenId, Scopes.Profile],
            ["vk"] = [Scopes.Email],
            ["yandex"] = ["login:avatar", "login:email", "login:info"]
        };

        foreach (var registration in options.Registrations)
        {
            var provider = Assert.IsType<string>(registration.ProviderName);
            Assert.Equal(
                $"https://accounts.example.test{CallbackPaths[provider]}",
                registration.RedirectUri!.AbsoluteUri);
            Assert.Equal(provider, registration.RegistrationId);
            Assert.Equal(
                expectedScopes[provider],
                registration.Scopes.Order(StringComparer.Ordinal));
            Assert.DoesNotContain(Scopes.OfflineAccess, registration.Scopes);
            Assert.False(string.IsNullOrWhiteSpace(registration.ClientId));
            Assert.False(string.IsNullOrWhiteSpace(registration.ClientSecret));
        }

        Assert.Contains(
            CodeChallengeMethods.Sha256,
            options.Registrations.Single(value => value.ProviderName == "github")
                .Configuration!.CodeChallengeMethodsSupported);
        Assert.Contains(
            CodeChallengeMethods.Sha256,
            options.Registrations.Single(value => value.ProviderName == "vk")
                .Configuration!.CodeChallengeMethodsSupported);
        Assert.Equal(
            OpenIddictClientWebIntegrationConstants.ProviderTypes.VkId,
            options.Registrations.Single(value => value.ProviderName == "vk")
                .ProviderType);
        Assert.Equal(
            OpenIddictClientWebIntegrationConstants.ProviderTypes.Yandex,
            options.Registrations.Single(value => value.ProviderName == "yandex")
                .ProviderType);

        var dataProtection = services
            .GetRequiredService<IOptions<OpenIddictClientDataProtectionOptions>>()
            .Value;
        Assert.False(dataProtection.PreferDefaultStateTokenFormat);
        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(
                assembly.GetName().Name,
                "OpenIddict.Server",
                StringComparison.Ordinal));
    }

    [Fact]
    public void IncompleteProviderBlockFailsClosedWithoutDisclosingValues()
    {
        const string clientId = "not-sensitive-but-must-not-be-echoed";
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["ExternalAuthentication:PublicOrigin"] =
                "https://accounts.example.test",
            ["ExternalAuthentication:Providers:Google:ClientId"] = clientId
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<ExternalAuthenticationOptions>>()
                .Value);

        Assert.DoesNotContain(clientId, exception.Message, StringComparison.Ordinal);
        Assert.Empty(services
            .GetRequiredService<IOptions<OpenIddictClientOptions>>()
            .Value
            .Registrations);
    }

    [Theory]
    [InlineData("http://accounts.example.test")]
    [InlineData("https://accounts.example.test/base")]
    [InlineData("https://user@accounts.example.test")]
    [InlineData("https://accounts.example.test?query=not-origin")]
    public void InvalidPublicOriginFailsClosed(string publicOrigin)
    {
        var configuration = CompleteConfiguration();
        configuration["ExternalAuthentication:PublicOrigin"] = publicOrigin;
        using var services = BuildServices(configuration);

        Assert.Throws<OptionsValidationException>(() =>
            services.GetRequiredService<IOptions<ExternalAuthenticationOptions>>()
                .Value);
    }

    [Fact]
    public void LoopbackHttpPublicOriginIsAcceptedForLocalDevelopment()
    {
        var configuration = CompleteConfiguration();
        configuration["ExternalAuthentication:PublicOrigin"] =
            "http://localhost:3000";
        using var services = BuildServices(configuration);

        var registrations = services
            .GetRequiredService<IOptions<OpenIddictClientOptions>>()
            .Value
            .Registrations;

        Assert.All(
            registrations,
            registration => Assert.StartsWith(
                "http://localhost:3000/api/auth/",
                registration.RedirectUri!.AbsoluteUri,
                StringComparison.Ordinal));
    }

    private static ServiceProvider BuildServices(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AuthDbContext>(options =>
            AuthDbContext.Configure(
                options,
                "Host=localhost;Database=unused;Username=unused;Password=unused"));
        services.AddOpenIddictExternalClient(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Dictionary<string, string?> CompleteConfiguration() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ExternalAuthentication:PublicOrigin"] =
                "https://accounts.example.test/",
            ["ExternalAuthentication:Providers:Google:ClientId"] = "google-id",
            ["ExternalAuthentication:Providers:Google:ClientSecret"] =
                "google-secret",
            ["ExternalAuthentication:Providers:GitHub:ClientId"] = "github-id",
            ["ExternalAuthentication:Providers:GitHub:ClientSecret"] =
                "github-secret",
            ["ExternalAuthentication:Providers:GitLab:ClientId"] = "gitlab-id",
            ["ExternalAuthentication:Providers:GitLab:ClientSecret"] =
                "gitlab-secret",
            ["ExternalAuthentication:Providers:Vk:ClientId"] = "vk-id",
            ["ExternalAuthentication:Providers:Vk:ClientSecret"] = "vk-secret",
            ["ExternalAuthentication:Providers:Yandex:ClientId"] = "yandex-id",
            ["ExternalAuthentication:Providers:Yandex:ClientSecret"] =
                "yandex-secret"
        };
}
