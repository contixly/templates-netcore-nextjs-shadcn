using System.Security.Cryptography;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;

namespace Template.Infrastructure.Authentication;

internal sealed class CryptographicLocalAutomationCredentialGenerator
    : ILocalAutomationCredentialGenerator
{
    public LocalAutomationCredentials Generate()
    {
        var seed = Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
            .ToLowerInvariant();
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        return new LocalAutomationCredentials(
            $"Local Automation {seed}",
            $"{LocalAutomationCredentialPolicy.EmailPrefix}{seed}@{LocalAutomationCredentialPolicy.EmailDomain}",
            $"local-{password}");
    }
}
