using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;
using Template.Infrastructure.ApiKeys;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeyStoreTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Personal_and_organization_create_persist_only_safe_credential_material()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("create@keys.test");
        var organization = await fixture.CreateOrganizationAsync(actor, OrganizationRole.Owner);

        var personal = await fixture.CreateKeyAsync(actor, Owner(actor), Hash(1), "user_abcdefghijk");
        var organizational = await fixture.CreateKeyAsync(actor, Owner(organization), Hash(2), "org_abcdefghijkl");

        Assert.True(personal.Succeeded);
        Assert.True(organizational.Succeeded);
        await using var db = fixture.CreateDbContext();
        var rows = await db.ApiKeys.OrderBy(row => row.KeyStart).ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal([Hash(2), Hash(1)], rows.Select(row => row.KeyHash).ToArray());
        Assert.Equal(["org_abcdefghijkl", "user_abcdefghijk"], rows.Select(row => row.KeyStart).ToArray());
        Assert.DoesNotContain(typeof(ApiKeyEntity).GetProperties(), property => property.Name is "Key" or "Credential" or "Secret");
    }

    [Fact]
    public async Task List_uses_descending_stable_cursor_and_excludes_revoked_rows()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("list@keys.test");
        var created = new List<ApiKeyId>();
        for (var index = 0; index < 5; index++)
        {
            var result = await fixture.CreateKeyAsync(
                actor,
                Owner(actor),
                Hash(index + 10),
                "user_abcdefghijk",
                ApiKeyStoreFixture.Now);
            created.Add(result.Value!.Id);
        }
        var descending = created.OrderByDescending(id => id.Value).ToArray();
        await fixture.Store.RevokeAsync(
            new(actor, ApiKeyOwnerKind.User, null, descending[2]),
            TestContext.Current.CancellationToken);

        var first = await fixture.Store.ListAsync(new(actor, Owner(actor), null, 2), TestContext.Current.CancellationToken);
        var second = await fixture.Store.ListAsync(new(actor, Owner(actor), first.Value!.Next, 2), TestContext.Current.CancellationToken);

        var expected = descending.Where(id => id != descending[2]).ToArray();
        Assert.Equal(expected[..2], first.Value.Items.Select(item => item.Id));
        Assert.Equal(expected[2..], second.Value!.Items.Select(item => item.Id));
        Assert.Null(second.Value.Next);
        Assert.Equal(4, first.Value.Items.Concat(second.Value.Items).Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task Update_returns_unchanged_for_a_semantic_no_op()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("no-op@keys.test");
        var key = (await fixture.CreateKeyAsync(
            actor,
            Owner(actor),
            Hash(19),
            "user_abcdefghijk")).Value!;

        var result = await fixture.Store.UpdateAsync(
            new(actor, Owner(actor), key.Id, key.Name,
                null, null, null, null, null, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyFailure.Unchanged, result.Failure);
        Assert.Equal(key.UpdatedAt, (await fixture.ReadKeyAsync(key.Id)).UpdatedAt);
    }

    [Fact]
    public async Task Organization_management_rechecks_role_and_owner_qualification()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("owner@keys.test");
        var member = await fixture.CreateUserAsync("member@keys.test");
        var foreignOwner = await fixture.CreateUserAsync("foreign@keys.test");
        var organization = await fixture.CreateOrganizationAsync(owner, OrganizationRole.Owner);
        await fixture.AddMemberAsync(organization, member, OrganizationRole.Member);
        var foreignOrganization = await fixture.CreateOrganizationAsync(foreignOwner, OrganizationRole.Owner);
        var key = (await fixture.CreateKeyAsync(owner, Owner(organization), Hash(20), "org_abcdefghijkl")).Value!;

        var deniedList = await fixture.Store.ListAsync(new(member, Owner(organization), null, 20), TestContext.Current.CancellationToken);
        var deniedCreate = await fixture.CreateKeyAsync(member, Owner(organization), Hash(21), "org_bcdefghijklm");
        var deniedUpdate = await fixture.Store.UpdateAsync(Update(member, organization, key.Id, "Denied"), TestContext.Current.CancellationToken);
        var deniedRevoke = await fixture.Store.RevokeAsync(new(member, ApiKeyOwnerKind.Organization, organization, key.Id), TestContext.Current.CancellationToken);
        var deniedRotate = await fixture.Store.RotateAsync(new(member, Owner(organization), key.Id, Hash(22), "org_cdefghijklmn"), TestContext.Current.CancellationToken);
        var foreign = await fixture.Store.UpdateAsync(Update(owner, foreignOrganization, key.Id, "Foreign"), TestContext.Current.CancellationToken);

        Assert.All(new[] { deniedList.Failure, deniedCreate.Failure, deniedUpdate.Failure, deniedRevoke.Failure, deniedRotate.Failure }, failure => Assert.Equal(ApiKeyFailure.PermissionDenied, failure));
        Assert.Equal(ApiKeyFailure.NotFound, foreign.Failure);

        await fixture.SetRoleAsync(organization, owner, OrganizationRole.Member);
        var staleOwner = await fixture.Store.UpdateAsync(Update(owner, organization, key.Id, "No longer owner"), TestContext.Current.CancellationToken);
        Assert.Equal(ApiKeyFailure.PermissionDenied, staleOwner.Failure);
    }

    [Fact]
    public async Task Rotation_preserves_identity_configuration_and_last_use_but_resets_window()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("rotate@keys.test");
        var originalHash = Hash(30);
        var key = (await fixture.CreateKeyAsync(actor, Owner(actor), originalHash, "user_abcdefghijk")).Value!;
        var usedAt = ApiKeyStoreFixture.Now.AddMinutes(2);
        fixture.SetTime(usedAt);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await fixture.Store.AuthenticateAndConsumeAsync(originalHash, TestContext.Current.CancellationToken)).Outcome);

        var rotatedAt = usedAt.AddMinutes(1);
        fixture.SetTime(rotatedAt);
        var rotated = await fixture.Store.RotateAsync(new(actor, Owner(actor), key.Id, Hash(31), "user_bcdefghijkl"), TestContext.Current.CancellationToken);

        Assert.True(rotated.Succeeded);
        Assert.Equal(key.Id, rotated.Value!.Id);
        Assert.Equal(key.Name, rotated.Value.Name);
        Assert.Equal(key.Scopes, rotated.Value.Scopes);
        Assert.Equal(0, rotated.Value.RequestCount);
        Assert.Null(rotated.Value.WindowStartedAt);
        Assert.Equal(usedAt, rotated.Value.LastRequestAt);
        Assert.Equal(rotatedAt, rotated.Value.RotatedAt);
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, (await fixture.Store.AuthenticateAndConsumeAsync(originalHash, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await fixture.Store.AuthenticateAndConsumeAsync(Hash(31), TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task Backward_clock_does_not_regress_committed_key_timestamps()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("backward-clock@keys.test");
        var originalHash = Hash(32);
        var key = (await fixture.CreateKeyAsync(
            actor,
            Owner(actor),
            originalHash,
            "user_abcdefghijk")).Value!;
        var usedAt = ApiKeyStoreFixture.Now.AddMinutes(5);
        fixture.SetTime(usedAt);
        Assert.Equal(
            ApiKeyAuthenticationOutcome.Succeeded,
            (await fixture.Store.AuthenticateAndConsumeAsync(
                originalHash,
                TestContext.Current.CancellationToken)).Outcome);

        var newestCommittedAt = ApiKeyStoreFixture.Now.AddMinutes(10);
        fixture.SetTime(newestCommittedAt);
        var firstRotation = await fixture.Store.RotateAsync(
            new(actor, Owner(actor), key.Id, Hash(33), "user_bcdefghijkl"),
            TestContext.Current.CancellationToken);
        Assert.Equal(newestCommittedAt, firstRotation.Value!.RotatedAt);

        fixture.SetTime(ApiKeyStoreFixture.Now.AddMinutes(2));
        var update = await fixture.Store.UpdateAsync(
            new(
                actor,
                Owner(actor),
                key.Id,
                "Renamed after clock rollback",
                null,
                null,
                null,
                null,
                null,
                null),
            TestContext.Current.CancellationToken);
        var secondRotation = await fixture.Store.RotateAsync(
            new(actor, Owner(actor), key.Id, Hash(34), "user_cdefghijklm"),
            TestContext.Current.CancellationToken);
        var authenticated = await fixture.Store.AuthenticateAndConsumeAsync(
            Hash(34),
            TestContext.Current.CancellationToken);
        var revoked = await fixture.Store.RevokeAsync(
            new(actor, ApiKeyOwnerKind.User, null, key.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(newestCommittedAt, update.Value!.UpdatedAt);
        Assert.Equal(newestCommittedAt, secondRotation.Value!.RotatedAt);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, authenticated.Outcome);
        Assert.Equal(newestCommittedAt, (await fixture.ReadKeyAsync(key.Id)).LastRequestAt);
        Assert.Equal(newestCommittedAt, revoked.Value!.RevokedAt);
    }

    [Fact]
    public async Task Revoke_is_terminal_and_user_and_organization_foreign_keys_cascade_exactly()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var creator = await fixture.CreateUserAsync("creator@keys.test");
        var remainingOwner = await fixture.CreateUserAsync("remaining@keys.test");
        var organization = await fixture.CreateOrganizationAsync(creator, OrganizationRole.Owner);
        await fixture.AddMemberAsync(organization, remainingOwner, OrganizationRole.Owner);
        var personal = (await fixture.CreateKeyAsync(creator, Owner(creator), Hash(40), "user_abcdefghijk")).Value!;
        var orgKey = (await fixture.CreateKeyAsync(creator, Owner(organization), Hash(41), "org_abcdefghijkl")).Value!;

        var revoked = await fixture.Store.RevokeAsync(new(creator, ApiKeyOwnerKind.User, null, personal.Id), TestContext.Current.CancellationToken);
        Assert.True(revoked.Succeeded);
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, (await fixture.Store.AuthenticateAndConsumeAsync(Hash(40), TestContext.Current.CancellationToken)).Outcome);

        await fixture.DeleteUserAsync(creator);
        Assert.False(await fixture.KeyExistsAsync(personal.Id));
        Assert.True(await fixture.KeyExistsAsync(orgKey.Id));
        await fixture.DeleteOrganizationAsync(organization);
        Assert.False(await fixture.KeyExistsAsync(orgKey.Id));
    }

    private static ApiKeyOwner Owner(UserId user) => new(ApiKeyOwnerKind.User, user, null);
    private static ApiKeyOwner Owner(OrganizationId organization) => new(ApiKeyOwnerKind.Organization, null, organization);
    private static byte[] Hash(int value) => Enumerable.Repeat((byte)value, 32).ToArray();
    private static UpdateApiKeyStoreCommand Update(UserId actor, OrganizationId organization, ApiKeyId key, string name) =>
        new(actor, Owner(organization), key, name, null, null, null, null, null, null);
}

internal sealed class ApiKeyStoreFixture : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainerFixture _postgres;
    private readonly string _databaseName;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;

    private ApiKeyStoreFixture(PostgreSqlContainerFixture postgres, string databaseName, string connectionString, ServiceProvider services)
    {
        _postgres = postgres;
        _databaseName = databaseName;
        _connectionString = connectionString;
        _services = services;
        _scope = services.CreateAsyncScope();
    }

    internal IApiKeyStore Store => _scope.ServiceProvider.GetRequiredService<IApiKeyStore>();

    internal static async Task<ApiKeyStoreFixture> CreateAsync(PostgreSqlContainerFixture postgres)
    {
        var database = await postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = database.ConnectionString,
            ["DataProtection:ApplicationName"] = "Template"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new MutableApiKeyTimeProvider(Now));
        services.AddSingleton<TimeProvider>(provider =>
            provider.GetRequiredService<MutableApiKeyTimeProvider>());
        services.AddSingleton<ApiKeyFailureInterceptor>();
        services.AddSingleton<ApiKeyTransactionBarrier>();
        services.AddDbContext<TemplateDbContext>((provider, options) =>
            options.AddInterceptors(
                provider.GetRequiredService<ApiKeyFailureInterceptor>(),
                provider.GetRequiredService<ApiKeyTransactionBarrier>()));
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
        var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TemplateDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        }
        return new(postgres, database.DatabaseName, database.ConnectionString, provider);
    }

    internal TemplateDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new(options.Options);
    }

    internal async Task<UserId> CreateUserAsync(string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = email,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = Now,
            UpdatedAt = Now
        };
        await using var db = CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(user.Id);
    }

    internal async Task<OrganizationId> CreateOrganizationAsync(UserId actor, OrganizationRole role)
    {
        var id = OrganizationId.New();
        await using var db = CreateDbContext();
        db.Organizations.Add(new OrganizationEntity { Id = id.Value, Name = $"Organization {id.Value:N}", Slug = $"o-{id.Value:N}", CreatedAt = Now, UpdatedAt = Now });
        db.OrganizationMembers.Add(new OrganizationMemberEntity { Id = Guid.CreateVersion7(), OrganizationId = id.Value, UserId = actor.Value, Role = role.Value, JoinedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task AddMemberAsync(OrganizationId organization, UserId user, OrganizationRole role)
    {
        await using var db = CreateDbContext();
        db.OrganizationMembers.Add(new OrganizationMemberEntity { Id = Guid.CreateVersion7(), OrganizationId = organization.Value, UserId = user.Value, Role = role.Value, JoinedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal async Task SetRoleAsync(OrganizationId organization, UserId user, OrganizationRole role)
    {
        await using var db = CreateDbContext();
        var member = await db.OrganizationMembers.SingleAsync(row => row.OrganizationId == organization.Value && row.UserId == user.Value, TestContext.Current.CancellationToken);
        member.Role = role.Value;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyOperationResult<ApiKeySummary>> CreateKeyAsync(
        UserId actor,
        ApiKeyOwner owner,
        byte[] hash,
        string start,
        DateTimeOffset? createdAt = null,
        int rateLimitMax = 10,
        DateTimeOffset? expiresAt = null)
    {
        var baseTime = createdAt ??
            _services.GetRequiredService<MutableApiKeyTimeProvider>().GetUtcNow();
        if (createdAt is not null)
        {
            SetTime(baseTime);
        }

        return await Store.CreateAsync(
            new(
                actor,
                owner,
                "Test key",
                [ApiKeyScopes.BasicRead],
                new ApiKeyExpiration(expiresAt - baseTime),
                true,
                rateLimitMax,
                TimeSpan.FromMinutes(1),
                hash,
                start),
            TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyOperationResult<ApiKeySummary>> CreateKeyInNewScopeAsync(
        UserId actor,
        ApiKeyOwner owner,
        byte[] hash,
        string start,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? createdAt = null)
    {
        var baseTime = createdAt ??
            _services.GetRequiredService<MutableApiKeyTimeProvider>().GetUtcNow();
        if (createdAt is not null)
        {
            SetTime(baseTime);
        }

        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>()
            .CreateAsync(
                new(actor, owner, "Test key", [ApiKeyScopes.BasicRead],
                    new ApiKeyExpiration(expiresAt - baseTime),
                    true, 10, TimeSpan.FromMinutes(1), hash, start),
                TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyAuthenticationResult> AuthenticateInNewScopeAsync(byte[] hash)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>().AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken);
    }

    internal void CoordinateAuthenticationStarts(int participants) =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .CoordinateAuthenticationStarts(participants);

    internal int CoordinatedAuthenticationArrivals =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .AuthenticationArrivals;

    internal void HoldTransactionAfterKeyLock(
        ApiKeyTransactionKind holder,
        ApiKeyTransactionKind contender) =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .HoldAfterKeyLock(holder, contender);

    internal void PauseNextAuthenticationBeforeKeyLock() =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .PauseNextAuthenticationBeforeKeyLock();

    internal Task WaitForAuthenticationBeforeKeyLockAsync() =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .WaitForAuthenticationBeforeKeyLockAsync(
                TestContext.Current.CancellationToken);

    internal void ReleaseAuthenticationBeforeKeyLock() =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .ReleaseAuthenticationBeforeKeyLock();

    internal Task WaitForHeldKeyLockAsync() =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .WaitForHolderAsync(TestContext.Current.CancellationToken);

    internal Task WaitForCompetingKeyLockStartAsync() =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>()
            .WaitForContenderAsync(TestContext.Current.CancellationToken);

    internal void ReleaseHeldTransaction() =>
        _services.GetRequiredService<ApiKeyTransactionBarrier>().ReleaseHolder();

    internal void FailNextAuthenticationAttempts(
        int count,
        DateTimeOffset? advanceTimeTo = null) =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .FailNextAuthenticationAttempts(count, advanceTimeTo);

    internal int AuthenticationAttempts =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>().AuthenticationAttempts;

    internal int AuthenticationTransactionCount =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>().AuthenticationTransactionCount;

    internal void FailNextPersonalManagementInserts(
        string sqlState,
        int count,
        DateTimeOffset? advanceTimeTo = null) =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .FailNextPersonalManagementInserts(
                sqlState,
                count,
                advanceTimeTo);

    internal void ObservePersonalManagement() =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .ObservePersonalManagement();

    internal void ObserveOrganizationManagement() =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .ObserveOrganizationManagement();

    internal void FailFirstOrganizationInsertAndPauseSecondAttempt(string sqlState) =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .FailFirstOrganizationInsertAndPauseSecondAttempt(sqlState);

    internal int PersonalManagementLockAttempts =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .PersonalManagementLockAttempts;

    internal int OrganizationManagementLockAttempts =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .OrganizationManagementLockAttempts;

    internal int MembershipAuthorizationAttempts =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .MembershipAuthorizationAttempts;

    internal int ManagementTransactionCount =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .ManagementTransactionCount;

    internal Task WaitForSecondOrganizationAttemptAsync() =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .WaitForSecondOrganizationAttemptAsync(TestContext.Current.CancellationToken);

    internal void ReleaseSecondOrganizationAttempt() =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>()
            .ReleaseSecondOrganizationAttempt();

    internal async Task<ApiKeyOperationResult<ApiKeySummary>> RotateInNewScopeAsync(
        UserId actor,
        ApiKeyId id,
        byte[] hash,
        string start)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>()
            .RotateAsync(
                new(
                    actor,
                    new(ApiKeyOwnerKind.User, actor, null),
                    id,
                    hash,
                    start),
                TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyOperationResult<ApiKeySummary>>
        UpdateExpirationInNewScopeAsync(
            UserId actor,
            ApiKeyId id,
            TimeSpan duration)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>()
            .UpdateAsync(
                new UpdateApiKeyStoreCommand(
                    actor,
                    new(ApiKeyOwnerKind.User, actor, null),
                    id,
                    null,
                    null,
                    new ApiKeyExpiration(duration),
                    null,
                    null,
                    null,
                    null),
                TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeInNewScopeAsync(UserId actor, ApiKeyId id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>().RevokeAsync(new(actor, ApiKeyOwnerKind.User, null, id), TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyMutationTestResult> MutateInNewScopeAsync(
        ApiKeyMutationKind kind,
        UserId actor,
        ApiKeyId id,
        byte[] replacementHash)
    {
        await using var scope = _services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IApiKeyStore>();
        if (kind == ApiKeyMutationKind.Rotate)
        {
            var result = await store.RotateAsync(
                new(actor, new(ApiKeyOwnerKind.User, actor, null), id,
                    replacementHash, "user_bcdefghijkl"),
                TestContext.Current.CancellationToken);
            return new(result.Succeeded);
        }
        var revoked = await store.RevokeAsync(
            new(actor, ApiKeyOwnerKind.User, null, id),
            TestContext.Current.CancellationToken);
        return new(revoked.Succeeded);
    }

    internal async Task<ApiKeyEntity> ReadKeyAsync(ApiKeyId id)
    {
        await using var db = CreateDbContext();
        return await db.ApiKeys.AsNoTracking().SingleAsync(
            row => row.Id == id.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> RequestCountAsync(byte[] hash) => (await QuotaStateAsync(hash)).RequestCount;

    internal async Task<(
        DateTimeOffset? WindowStartedAt,
        DateTimeOffset? LastRequestAt,
        int RequestCount)> TemporalStateAsync(byte[] hash)
    {
        await using var db = CreateDbContext();
        return await db.ApiKeys.Where(row => row.KeyHash == hash)
            .Select(row => new ValueTuple<DateTimeOffset?, DateTimeOffset?, int>(
                row.WindowStartedAt,
                row.LastRequestAt,
                row.RequestCount))
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<(DateTimeOffset? WindowStartedAt, int RequestCount)> QuotaStateAsync(byte[] hash)
    {
        await using var db = CreateDbContext();
        return await db.ApiKeys.Where(row => row.KeyHash == hash).Select(row => new ValueTuple<DateTimeOffset?, int>(row.WindowStartedAt, row.RequestCount)).SingleAsync(TestContext.Current.CancellationToken);
    }

    internal async Task SetTerminalStateAsync(ApiKeyId id, bool enabled, DateTimeOffset? expiresAt, DateTimeOffset? revokedAt)
    {
        await using var db = CreateDbContext();
        var key = await db.ApiKeys.SingleAsync(row => row.Id == id.Value, TestContext.Current.CancellationToken);
        key.Enabled = enabled;
        key.ExpiresAt = expiresAt;
        key.RevokedAt = revokedAt;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal async Task DeleteUserAsync(UserId user)
    {
        await using var db = CreateDbContext();
        await db.Users.Where(row => row.Id == user.Value).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    internal async Task DeleteOrganizationAsync(OrganizationId organization)
    {
        await using var db = CreateDbContext();
        await db.Organizations.Where(row => row.Id == organization.Value).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<bool> KeyExistsAsync(ApiKeyId id)
    {
        await using var db = CreateDbContext();
        return await db.ApiKeys.AnyAsync(row => row.Id == id.Value, TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountKeysAsync()
    {
        await using var db = CreateDbContext();
        return await db.ApiKeys.CountAsync(TestContext.Current.CancellationToken);
    }

    internal void SetTime(DateTimeOffset utcNow) =>
        _services.GetRequiredService<MutableApiKeyTimeProvider>()
            .SetUtcNow(utcNow);

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _postgres.DropDatabaseAsync(_databaseName, TestContext.Current.CancellationToken);
    }
}

internal sealed record ApiKeyMutationTestResult(bool Succeeded);

internal sealed class MutableApiKeyTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _utcNow = now;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    internal void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            _utcNow = utcNow;
        }
    }
}
