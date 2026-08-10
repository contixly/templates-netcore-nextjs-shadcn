using Microsoft.Extensions.Options;
using Template.Domain.Accounts;

namespace Template.Infrastructure.Authentication;

public interface IExternalProviderCatalog
{
    IReadOnlyList<ExternalProviderDescriptor> Known { get; }

    bool IsConfigured(ExternalProvider provider);

    string GetAuthenticationScheme(ExternalProvider provider);
}

public sealed record ExternalProviderDescriptor(
    ExternalProvider Provider,
    string DisplayName,
    bool Configured);

internal sealed class ExternalProviderCatalog
    : IExternalProviderCatalog
{
    private readonly IReadOnlyDictionary<string, bool> _configured;

    public ExternalProviderCatalog(
        IOptions<ExternalAuthenticationOptions> options)
    {
        var value = options.Value;
        var hasOrigin = value.TryGetPublicOrigin(out _);
        Known = ExternalProviderMetadata.All
            .Select(provider => new ExternalProviderDescriptor(
                provider,
                ExternalProviderMetadata.GetDisplayName(provider),
                hasOrigin && (provider == ExternalProvider.Vk
                    ? value.TryGetClientId(provider, out _)
                    : value.TryGetCompleteCredentials(provider, out _))))
            .ToArray();
        _configured = Known.ToDictionary(
            descriptor => descriptor.Provider.Value,
            descriptor => descriptor.Configured,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<ExternalProviderDescriptor> Known { get; }

    public bool IsConfigured(ExternalProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return _configured.TryGetValue(provider.Value, out var configured)
            && configured;
    }

    public string GetAuthenticationScheme(ExternalProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_configured.ContainsKey(provider.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Only known external providers have authentication schemes.");
        }

        return provider.Value;
    }
}
