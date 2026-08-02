using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;
using Template.Infrastructure.ApiKeys;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeyCredentialTests
{
    private readonly IApiKeyCredentialService _credentials =
        new CryptographicApiKeyCredentialService();

    [Fact]
    public void Generate_creates_unique_user_credentials_with_canonical_hash_material()
    {
        var generated = Enumerable.Range(0, 1_000)
            .Select(_ => _credentials.Generate(ApiKeyOwnerKind.User))
            .ToArray();

        Assert.Equal(1_000, generated.Select(material => material.Credential).Distinct().Count());
        Assert.All(generated, material =>
        {
            Assert.StartsWith("user_", material.Credential, StringComparison.Ordinal);
            Assert.Equal(32, WebEncoders.Base64UrlDecode(material.Credential["user_".Length..]).Length);
            Assert.Equal(32, material.Hash.Length);
            Assert.Equal(16, material.Start.Length);
            Assert.True(_credentials.TryHashCanonical(material.Credential, out var hash));
            Assert.Equal(material.Hash, hash);
        });
    }

    [Fact]
    public void Generate_creates_organization_credentials_with_the_organization_prefix()
    {
        var material = _credentials.Generate(ApiKeyOwnerKind.Organization);

        Assert.StartsWith("org_", material.Credential, StringComparison.Ordinal);
        Assert.Equal(32, WebEncoders.Base64UrlDecode(material.Credential["org_".Length..]).Length);
        Assert.Equal(32, material.Hash.Length);
        Assert.Equal(16, material.Start.Length);
    }

    [Fact]
    public void Try_hash_canonical_fails_closed_for_noncanonical_credentials()
    {
        var credential = _credentials.Generate(ApiKeyOwnerKind.User).Credential;
        var invalidCredentials = new[]
        {
            $" {credential}",
            $"{credential}=",
            $"user-{credential["user_".Length..]}",
            $"user_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(31))}",
            credential + new string('A', 128)
        };

        foreach (var invalidCredential in invalidCredentials)
        {
            Assert.False(_credentials.TryHashCanonical(invalidCredential, out var hash));
            Assert.Empty(hash);
        }
    }
}
