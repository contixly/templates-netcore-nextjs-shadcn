using Microsoft.Extensions.Options;
using Template.Domain.Accounts;

namespace Template.Infrastructure.Authentication;

public sealed class ExternalAuthenticationOptions
{
    public const string SectionName = "ExternalAuthentication";

    public string? PublicOrigin { get; set; }

    public Dictionary<string, ExternalProviderCredentials> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal bool TryGetPublicOrigin(out Uri? origin)
    {
        origin = null;
        if (string.IsNullOrWhiteSpace(PublicOrigin)
            || !string.Equals(PublicOrigin, PublicOrigin.Trim(), StringComparison.Ordinal)
            || !Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var candidate)
            || string.IsNullOrEmpty(candidate.Host)
            || !string.IsNullOrEmpty(candidate.UserInfo)
            || !string.IsNullOrEmpty(candidate.Query)
            || !string.IsNullOrEmpty(candidate.Fragment)
            || candidate.AbsolutePath is not "/"
            || candidate.Scheme != Uri.UriSchemeHttp
                && candidate.Scheme != Uri.UriSchemeHttps
            || candidate.Scheme == Uri.UriSchemeHttp && !candidate.IsLoopback)
        {
            return false;
        }

        origin = new Uri(
            candidate.GetLeftPart(UriPartial.Authority) + "/",
            UriKind.Absolute);
        return true;
    }

    internal bool TryGetCompleteCredentials(
        ExternalProvider provider,
        out ExternalProviderCredentials? credentials)
    {
        credentials = FindCredentials(provider);
        return credentials is not null && credentials.IsComplete;
    }

    internal ExternalProviderCredentials? FindCredentials(
        ExternalProvider provider)
    {
        if (Providers is null)
        {
            return null;
        }

        var configurationName =
            ExternalProviderMetadata.GetConfigurationName(provider);
        return Providers.FirstOrDefault(pair =>
                string.Equals(
                    pair.Key,
                    configurationName,
                    StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}

public sealed class ExternalProviderCredentials
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    internal bool IsComplete =>
        IsConfiguredValue(ClientId) && IsConfiguredValue(ClientSecret);

    internal bool IsEntirelyAbsent =>
        ClientId is null && ClientSecret is null;

    private static bool IsConfiguredValue(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && string.Equals(value, value.Trim(), StringComparison.Ordinal);
}

internal sealed class ExternalAuthenticationOptionsValidator
    : IValidateOptions<ExternalAuthenticationOptions>
{
    private const string InvalidOriginMessage =
        "ExternalAuthentication:PublicOrigin must be an HTTPS origin or an HTTP loopback origin.";
    private const string InvalidProviderMessage =
        "ExternalAuthentication provider credentials must be supplied together for a known provider.";

    public ValidateOptionsResult Validate(
        string? name,
        ExternalAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var providers = options.Providers ??
            new Dictionary<string, ExternalProviderCredentials>();
        if (providers.Count > 0 || options.PublicOrigin is not null)
        {
            if (!options.TryGetPublicOrigin(out _))
            {
                return ValidateOptionsResult.Fail(InvalidOriginMessage);
            }
        }

        foreach (var (configurationName, credentials) in providers)
        {
            if (!ExternalProviderMetadata.TryFromConfigurationName(
                    configurationName,
                    out _)
                || credentials is null
                || credentials.IsEntirelyAbsent
                || !credentials.IsComplete)
            {
                return ValidateOptionsResult.Fail(InvalidProviderMessage);
            }
        }

        return ValidateOptionsResult.Success;
    }
}

internal static class ExternalProviderMetadata
{
    internal static readonly ExternalProvider[] All =
    [
        ExternalProvider.Google,
        ExternalProvider.GitHub,
        ExternalProvider.GitLab,
        ExternalProvider.Vk,
        ExternalProvider.Yandex
    ];

    internal static string GetConfigurationName(ExternalProvider provider) =>
        provider.Value switch
        {
            "google" => "Google",
            "github" => "GitHub",
            "gitlab" => "GitLab",
            "vk" => "Vk",
            "yandex" => "Yandex",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Only known external providers can be configured.")
        };

    internal static string GetDisplayName(ExternalProvider provider) =>
        provider.Value switch
        {
            "google" => "Google",
            "github" => "GitHub",
            "gitlab" => "GitLab",
            "vk" => "VK",
            "yandex" => "Yandex",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Only known external providers have display names.")
        };

    internal static string GetCallbackPath(ExternalProvider provider) =>
        provider.Value switch
        {
            "google" => "/api/auth/callback/google",
            "github" => "/api/auth/callback/github",
            "gitlab" => "/api/auth/callback/gitlab",
            "vk" => "/api/auth/callback/vk",
            "yandex" => "/api/auth/oauth2/callback/yandex",
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Only known external providers have callback paths.")
        };

    internal static bool TryFromConfigurationName(
        string value,
        out ExternalProvider? provider)
    {
        provider = All.FirstOrDefault(candidate =>
            string.Equals(
                GetConfigurationName(candidate),
                value,
                StringComparison.OrdinalIgnoreCase));
        return provider is not null;
    }
}
