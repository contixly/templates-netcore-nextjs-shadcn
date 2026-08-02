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
    public async Task Create_validation_failure_is_not_retried_and_never_generates_material()
    {
        var store = new RecordingStore();
        var credentials = new RecordingCredentials();
        var invalid = Create(ApiKeyOwnerKind.User, null) with { Name = "\u0001" };

        var result = await Service(store, credentials).CreateAsync(
            invalid,
            TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyFailure.InvalidName, result.Failure);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(0, credentials.GenerateCalls);
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

    [Fact]
    public async Task Create_retries_hash_collisions_with_fresh_material_and_reveals_only_the_persisted_credential()
    {
        var owner = new ApiKeyOwner(ApiKeyOwnerKind.User, Actor, null);
        var store = new RecordingStore
        {
            CreateResults =
            [
                ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.ConcurrencyConflict),
                ApiKeyOperationResult<ApiKeySummary>.Success(Summary(owner))
            ]
        };
        var credentials = new RecordingCredentials();

        var result = await Service(store, credentials).CreateAsync(
            Create(ApiKeyOwnerKind.User, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(2, store.CreateCalls);
        Assert.Equal(2, credentials.GenerateCalls);
        Assert.Equal(credentials.Generated[1].Hash, store.CreateCommands[1].Hash);
        Assert.Equal(credentials.Generated[1].Credential, result.Value!.Credential);
    }

    [Fact]
    public async Task Rotate_stops_after_three_hash_collisions()
    {
        var store = new RecordingStore
        {
            RotateResults = Enumerable.Repeat(
                ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.ConcurrencyConflict),
                3).ToArray()
        };
        var credentials = new RecordingCredentials();

        var result = await Service(store, credentials).RotateAsync(
            new(Actor, ApiKeyOwnerKind.User, null, Key),
            TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyFailure.ConcurrencyConflict, result.Failure);
        Assert.Equal(3, store.RotateCalls);
        Assert.Equal(3, credentials.GenerateCalls);
    }

    private static ApiKeyManagementService Service(RecordingStore store, RecordingCredentials credentials) => new(store, credentials, new FakeTimeProvider(Now));
    private static CreateApiKeyCommand Create(ApiKeyOwnerKind kind, OrganizationId? organization) => new(Actor, kind, organization, " Key ", ["basic-read"], "30d", true, 1000, "1h");
    private static ApiKeySummary Summary(ApiKeyOwner owner) => new(Key, owner, "Key", "safe-start", [ApiKeyScopes.BasicRead], true, true, 1000, TimeSpan.FromHours(1), 0, null, null, Now.AddDays(30), null, Now, Now);

    private sealed class RecordingCredentials : IApiKeyCredentialService
    {
        public List<ApiKeyCredentialMaterial> Generated { get; } = [];
        public int GenerateCalls => Generated.Count;
        public ApiKeyCredentialMaterial Material => Generated.Count == 0
            ? Generate(ApiKeyOwnerKind.User)
            : Generated[0];
        public ApiKeyCredentialMaterial Generate(ApiKeyOwnerKind ownerKind)
        {
            var index = Generated.Count + 1;
            var material = new ApiKeyCredentialMaterial($"credential-{index}", [(byte)index, 2], $"safe-start-{index}");
            Generated.Add(material);
            return material;
        }
        public bool TryHashCanonical(string credential, out byte[] hash) { hash = []; return false; }
    }

    private sealed class RecordingStore : IApiKeyStore
    {
        public CreateApiKeyStoreCommand? LastCreate { get; private set; }
        public UpdateApiKeyCommand? LastUpdate { get; private set; }
        public int UpdateCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public int RotateCalls { get; private set; }
        public List<CreateApiKeyStoreCommand> CreateCommands { get; } = [];
        public IReadOnlyList<ApiKeyOperationResult<ApiKeySummary>>? CreateResults { get; set; }
        public IReadOnlyList<ApiKeyOperationResult<ApiKeySummary>>? RotateResults { get; set; }
        public ApiKeyOperationResult<ApiKeySummary> CreateResult { get; set; } = ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
        public ApiKeyOperationResult<ApiKeySummary> UpdateResult { get; set; } = ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
        public ApiKeyOperationResult<ApiKeySummary> RotateResult { get; set; } = ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
        public ApiKeyOperationResult<ApiKeyRevocation> RevokeResult { get; set; } = ApiKeyOperationResult<ApiKeyRevocation>.Failed(ApiKeyFailure.NotFound);
        public Task<ApiKeyOperationResult<ApiKeyStorePage>> ListAsync(ApiKeyListQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ApiKeyOperationResult<ApiKeySummary>> CreateAsync(CreateApiKeyStoreCommand command, CancellationToken cancellationToken) { LastCreate = command; CreateCommands.Add(command); return Task.FromResult(CreateResults?[CreateCalls++] ?? IncrementCreate()); }
        public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(UpdateApiKeyCommand command, CancellationToken cancellationToken) { UpdateCalls++; LastUpdate = command; return Task.FromResult(UpdateResult); }
        public Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken) => Task.FromResult(RevokeResult);
        public Task<ApiKeyOperationResult<ApiKeySummary>> RotateAsync(RotateApiKeyStoreCommand command, CancellationToken cancellationToken) => Task.FromResult(RotateResults?[RotateCalls++] ?? IncrementRotate());
        public Task<ApiKeyAuthenticationResult> AuthenticateAndConsumeAsync(byte[] hash, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotImplementedException();

        private ApiKeyOperationResult<ApiKeySummary> IncrementCreate() { CreateCalls++; return CreateResult; }
        private ApiKeyOperationResult<ApiKeySummary> IncrementRotate() { RotateCalls++; return RotateResult; }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
