namespace Template.Application.ApiKeys.Ports;

public interface IApiKeyStore
{
    Task<ApiKeyOperationResult<ApiKeyStorePage>> ListAsync(ApiKeyListQuery query, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeySummary>> CreateAsync(CreateApiKeyStoreCommand command, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(UpdateApiKeyCommand command, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken);
    Task<ApiKeyOperationResult<ApiKeySummary>> RotateAsync(RotateApiKeyStoreCommand command, CancellationToken cancellationToken);
    Task<ApiKeyAuthenticationResult> AuthenticateAndConsumeAsync(byte[] hash, DateTimeOffset now, CancellationToken cancellationToken);
}
