using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;

namespace Template.Application.Tests.ApiKeys;

public sealed class ApiKeyAuthenticationServiceTests
{
    [Fact]
    public async Task Noncanonical_credentials_are_rejected_before_the_store()
    {
        var store = new AuthenticationStore();
        var result = await new ApiKeyAuthenticationService(new RejectingCredentials(), store)
            .AuthenticateAsync("noncanonical", TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, result.Outcome);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Valid_and_rate_limited_store_outcomes_are_mapped_without_disclosing_credentials()
    {
        var owner = new ApiKeyOwner(ApiKeyOwnerKind.User, new UserId(Guid.Parse("00000000-0000-0000-0000-000000000001")), null);
        var principal = new ApiKeyPrincipal(new(Guid.Parse("00000000-0000-0000-0000-000000000002")), "safe-start", owner, [ApiKeyScopes.BasicRead]);
        var store = new AuthenticationStore
        {
            Result = ApiKeyAuthenticationResult.Succeeded(principal)
        };
        var service = new ApiKeyAuthenticationService(new AcceptingCredentials(), store);

        var authenticated = await service.AuthenticateAsync("canonical", TestContext.Current.CancellationToken);
        store.Result = ApiKeyAuthenticationResult.RateLimited(
            principal,
            TimeSpan.FromDays(2));
        var limited = await service.AuthenticateAsync("canonical", TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, authenticated.Outcome);
        Assert.Equal(principal, authenticated.Principal);
        Assert.Equal(ApiKeyAuthenticationOutcome.RateLimited, limited.Outcome);
        Assert.Equal(principal, limited.Principal);
        Assert.Equal(TimeSpan.FromDays(1), limited.RetryAfter);
    }

    private sealed class RejectingCredentials : IApiKeyCredentialService
    {
        public ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind) => throw new NotImplementedException();
        public bool TryHashCanonical(string credential, out byte[] hash) { hash = []; return false; }
    }
    private sealed class AcceptingCredentials : IApiKeyCredentialService
    {
        public ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind) => throw new NotImplementedException();
        public bool TryHashCanonical(string credential, out byte[] hash) { hash = [1]; return true; }
    }
    private sealed class AuthenticationStore : IApiKeyStore
    {
        public int Calls { get; private set; }
        public ApiKeyAuthenticationResult Result { get; set; } = ApiKeyAuthenticationResult.Invalid();
        public Task<ApiKeyAuthenticationResult> AuthenticateAndConsumeAsync(byte[] hash, CancellationToken cancellationToken) { Calls++; return Task.FromResult(Result); }
        public Task<ApiKeyOperationResult<ApiKeyStorePage>> ListAsync(ApiKeyListQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ApiKeyOperationResult<ApiKeySummary>> CreateAsync(CreateApiKeyStoreCommand command, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(UpdateApiKeyStoreCommand command, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ApiKeyOperationResult<ApiKeySummary>> RotateAsync(RotateApiKeyStoreCommand command, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
