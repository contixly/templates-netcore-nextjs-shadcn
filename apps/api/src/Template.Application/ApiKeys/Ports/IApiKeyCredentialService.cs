using Template.Domain.ApiKeys;

namespace Template.Application.ApiKeys.Ports;

public sealed record ApiKeyCredentialMaterial(string Credential, byte[] Hash, string Start);

public interface IApiKeyCredentialService
{
    ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind);
    bool TryHashCanonical(string credential, out byte[] hash);
}
