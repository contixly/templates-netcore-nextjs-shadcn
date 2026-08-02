using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;

namespace Template.Infrastructure.ApiKeys;

public sealed class CryptographicApiKeyCredentialService : IApiKeyCredentialService
{
    private const int SecretLength = 32;
    private const int StartLength = 16;
    private const string UserPrefix = "user_";
    private const string OrganizationPrefix = "org_";
    private const int EncodedSecretLength = 43;

    public ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind)
    {
        var prefix = ownerKind switch
        {
            ApiKeyOwnerKind.User => UserPrefix,
            ApiKeyOwnerKind.Organization => OrganizationPrefix,
            _ => throw new ArgumentOutOfRangeException(nameof(ownerKind))
        };
        var credential = prefix + WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(SecretLength));

        return new(
            credential,
            SHA256.HashData(Encoding.UTF8.GetBytes(credential)),
            credential[..StartLength]);
    }

    public bool TryHashCanonical(string credential, out byte[] hash)
    {
        hash = [];
        if (credential is null || !TryGetPayload(credential, out var payload))
        {
            return false;
        }

        hash = SHA256.HashData(Encoding.UTF8.GetBytes(credential));
        return true;
    }

    private static bool TryGetPayload(string credential, out string payload)
    {
        payload = string.Empty;
        var prefixLength = credential.StartsWith(UserPrefix, StringComparison.Ordinal)
            ? UserPrefix.Length
            : credential.StartsWith(OrganizationPrefix, StringComparison.Ordinal)
                ? OrganizationPrefix.Length
                : 0;
        if (prefixLength == 0 || credential.Length != prefixLength + EncodedSecretLength)
        {
            return false;
        }

        payload = credential[prefixLength..];
        if (payload.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            var secret = WebEncoders.Base64UrlDecode(payload);
            return secret.Length == SecretLength
                && string.Equals(
                    WebEncoders.Base64UrlEncode(secret),
                    payload,
                    StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
