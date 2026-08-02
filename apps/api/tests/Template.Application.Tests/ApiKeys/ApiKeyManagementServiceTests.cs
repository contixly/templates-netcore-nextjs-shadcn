using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Tests.ApiKeys;

public sealed class ApiKeyManagementServiceTests
{
    private static readonly UserId Actor = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly ApiKeyId Key = new(Guid.Parse("00000000-0000-0000-0000-000000000003"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public async Task Personal_create_derives_the_user_owner_from_the_actor_and_never_passes_credential_to_the_store()
    {
        var store = new RecordingStore { CreateResult = ApiKeyOperationResult<ApiKeySummary>.Success(Summary(new(ApiKeyOwnerKind.User, Actor, null))) };
        var credentials = new RecordingCredentials();
        var result = await Service(store, credentials).CreateAsync(Create(ApiKeyOwnerKind.User, null), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(Actor, store.LastCreate!.ActorUserId);
        Assert.Equal(new ApiKeyOwner(ApiKeyOwnerKind.User, Actor, null), store.LastCreate.Owner);
        Assert.Equal(credentials.Material.Hash, store.LastCreate.Hash);
        Assert.Equal(credentials.Material.Start, store.LastCreate.Start);
        Assert.Equal(credentials.Material.Credential, result.Value!.Credential);
    }

    [Fact]
    public async Task Organization_commands_keep_actor_and_owner_separate()
    {
        var store = new RecordingStore { UpdateResult = ApiKeyOperationResult<ApiKeySummary>.Success(Summary(new(ApiKeyOwnerKind.Organization, null, Organization))) };
        var command = new UpdateApiKeyCommand(Actor, ApiKeyOwnerKind.Organization, Organization, Key, " Renamed ", null, null, null, null, null, null);
        await Service(store, new RecordingCredentials()).UpdateAsync(command, TestContext.Current.CancellationToken);

        Assert.Equal(Actor, store.LastUpdate!.ActorUserId);
        Assert.Equal(new ApiKeyOwner(ApiKeyOwnerKind.Organization, null, Organization), store.LastUpdate.Owner);
    }

    [Fact]
    public async Task Update_rejects_an_empty_patch_without_store_access()
    {
        var store = new RecordingStore();
        var result = await Service(store, new RecordingCredentials()).UpdateAsync(
            new(Actor, ApiKeyOwnerKind.User, null, Key, null, null, null, null, null, null, null), TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyFailure.Unchanged, result.Failure);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task Rotate_returns_the_replacement_credential_and_revoke_propagates_terminal_failure()
    {
        var owner = new ApiKeyOwner(ApiKeyOwnerKind.User, Actor, null);
        var store = new RecordingStore
        {
            RotateResult = ApiKeyOperationResult<ApiKeySummary>.Success(Summary(owner)),
            RevokeResult = ApiKeyOperationResult<ApiKeyRevocation>.Failed(ApiKeyFailure.NotFound)
        };
        var credentials = new RecordingCredentials();
        var service = Service(store, credentials);

        var rotated = await service.RotateAsync(new(Actor, ApiKeyOwnerKind.User, null, Key), TestContext.Current.CancellationToken);
        var revoked = await service.RevokeAsync(new(Actor, ApiKeyOwnerKind.User, null, Key), TestContext.Current.CancellationToken);

        Assert.Equal(credentials.Material.Credential, rotated.Value!.Credential);
        Assert.Equal(ApiKeyFailure.NotFound, revoked.Failure);
    }

    private static ApiKeyManagementService Service(RecordingStore store, RecordingCredentials credentials) => new(store, credentials, new FakeTimeProvider(Now));
    private static CreateApiKeyCommand Create(ApiKeyOwnerKind kind, OrganizationId? organization) => new(Actor, kind, organization, " Key ", ["basic-read"], "30d", true, 1000, "1h");
    private static ApiKeySummary Summary(ApiKeyOwner owner) => new(Key, owner, "Key", "safe-start", [ApiKeyScopes.BasicRead], true, true, 1000, TimeSpan.FromHours(1), 0, null, null, Now.AddDays(30), null, Now, Now);

    private sealed class RecordingCredentials : IApiKeyCredentialService
    {
        public ApiKeyCredentialMaterial Material { get; } = new("credential-is-not-in-an-assertion", [1, 2], "safe-start");
        public ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind) => Material;
        public bool TryHashCanonical(string credential, out byte[] hash) { hash = []; return false; }
    }

    private sealed class RecordingStore : IApiKeyStore
    {
        public CreateApiKeyStoreCommand? LastCreate { get; private set; }
        public UpdateApiKeyCommand? LastUpdate { get; private set; }
        public int UpdateCalls { get; private set; }
        public ApiKeyOperationResult<ApiKeySummary> CreateResult { get; set; } = ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
        public ApiKeyOperationResult<ApiKeySummary> UpdateResult { get; set; } = ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
        public ApiKeyOperationResult<ApiKeySummary> RotateResult { get; set; } = ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
        public ApiKeyOperationResult<ApiKeyRevocation> RevokeResult { get; set; } = ApiKeyOperationResult<ApiKeyRevocation>.Failed(ApiKeyFailure.NotFound);
        public Task<ApiKeyOperationResult<ApiKeyStorePage>> ListAsync(ApiKeyListQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ApiKeyOperationResult<ApiKeySummary>> CreateAsync(CreateApiKeyStoreCommand command, CancellationToken cancellationToken) { LastCreate = command; return Task.FromResult(CreateResult); }
        public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(UpdateApiKeyCommand command, CancellationToken cancellationToken) { UpdateCalls++; LastUpdate = command; return Task.FromResult(UpdateResult); }
        public Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken) => Task.FromResult(RevokeResult);
        public Task<ApiKeyOperationResult<ApiKeySummary>> RotateAsync(RotateApiKeyStoreCommand command, CancellationToken cancellationToken) => Task.FromResult(RotateResult);
        public Task<ApiKeyAuthenticationResult> AuthenticateAndConsumeAsync(byte[] hash, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
