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
            var result = await fixture.CreateKeyAsync(actor, Owner(actor), Hash(index + 10), "user_abcdefghijk", ApiKeyStoreFixture.Now.AddMinutes(index));
            created.Add(result.Value!.Id);
        }
        await fixture.Store.RevokeAsync(new(actor, ApiKeyOwnerKind.User, null, created[2]), TestContext.Current.CancellationToken);

        var first = await fixture.Store.ListAsync(new(actor, Owner(actor), null, 2), TestContext.Current.CancellationToken);
        var second = await fixture.Store.ListAsync(new(actor, Owner(actor), first.Value!.Next, 2), TestContext.Current.CancellationToken);

        Assert.Equal([created[4], created[3]], first.Value.Items.Select(item => item.Id));
        Assert.Equal([created[1], created[0]], second.Value!.Items.Select(item => item.Id));
        Assert.Null(second.Value.Next);
        Assert.Equal(4, first.Value.Items.Concat(second.Value.Items).Select(item => item.Id).Distinct().Count());
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
        var deniedRotate = await fixture.Store.RotateAsync(new(member, Owner(organization), key.Id, Hash(22), "org_cdefghijklmn", ApiKeyStoreFixture.Now), TestContext.Current.CancellationToken);
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
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await fixture.Store.AuthenticateAndConsumeAsync(originalHash, usedAt, TestContext.Current.CancellationToken)).Outcome);

        var rotatedAt = usedAt.AddMinutes(1);
        var rotated = await fixture.Store.RotateAsync(new(actor, Owner(actor), key.Id, Hash(31), "user_bcdefghijkl", rotatedAt), TestContext.Current.CancellationToken);

        Assert.True(rotated.Succeeded);
        Assert.Equal(key.Id, rotated.Value!.Id);
        Assert.Equal(key.Name, rotated.Value.Name);
        Assert.Equal(key.Scopes, rotated.Value.Scopes);
        Assert.Equal(0, rotated.Value.RequestCount);
        Assert.Null(rotated.Value.WindowStartedAt);
        Assert.Equal(usedAt, rotated.Value.LastRequestAt);
        Assert.Equal(rotatedAt, rotated.Value.RotatedAt);
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, (await fixture.Store.AuthenticateAndConsumeAsync(originalHash, rotatedAt, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await fixture.Store.AuthenticateAndConsumeAsync(Hash(31), rotatedAt, TestContext.Current.CancellationToken)).Outcome);
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
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, (await fixture.Store.AuthenticateAndConsumeAsync(Hash(40), ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken)).Outcome);

        await fixture.DeleteUserAsync(creator);
        Assert.False(await fixture.KeyExistsAsync(personal.Id));
        Assert.True(await fixture.KeyExistsAsync(orgKey.Id));
        await fixture.DeleteOrganizationAsync(organization);
        Assert.False(await fixture.KeyExistsAsync(orgKey.Id));
    }

    private static ApiKeyOwner Owner(UserId user) => new(ApiKeyOwnerKind.User, user, null);
    private static ApiKeyOwner Owner(OrganizationId organization) => new(ApiKeyOwnerKind.Organization, null, organization);
    private static byte[] Hash(int value) => Enumerable.Repeat((byte)value, 32).ToArray();
    private static UpdateApiKeyCommand Update(UserId actor, OrganizationId organization, ApiKeyId key, string name) =>
        new(actor, ApiKeyOwnerKind.Organization, organization, key, name, null, null, null, null, null, null);
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
        services.AddSingleton<TimeProvider>(new FixedApiKeyTimeProvider(Now));
        services.AddSingleton<ApiKeyFailureInterceptor>();
        services.AddDbContext<TemplateDbContext>((provider, options) =>
            options.AddInterceptors(provider.GetRequiredService<ApiKeyFailureInterceptor>()));
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

    internal Task<ApiKeyOperationResult<ApiKeySummary>> CreateKeyAsync(UserId actor, ApiKeyOwner owner, byte[] hash, string start, DateTimeOffset? createdAt = null, int rateLimitMax = 10) =>
        Store.CreateAsync(new(actor, owner, "Test key", [ApiKeyScopes.BasicRead], null, true, rateLimitMax, TimeSpan.FromMinutes(1), hash, start, createdAt ?? Now), TestContext.Current.CancellationToken);

    internal async Task<ApiKeyAuthenticationResult> AuthenticateInNewScopeAsync(byte[] hash, DateTimeOffset now)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>().AuthenticateAndConsumeAsync(hash, now, TestContext.Current.CancellationToken);
    }

    internal void FailNextAuthenticationAttempts(int count) =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>().FailNextAuthenticationAttempts(count);

    internal int AuthenticationAttempts =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>().AuthenticationAttempts;

    internal int AuthenticationTransactionCount =>
        _services.GetRequiredService<ApiKeyFailureInterceptor>().AuthenticationTransactionCount;

    internal async Task<ApiKeyOperationResult<ApiKeySummary>> RotateInNewScopeAsync(UserId actor, ApiKeyId id, byte[] hash, string start)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>().RotateAsync(new(actor, new(ApiKeyOwnerKind.User, actor, null), id, hash, start, Now), TestContext.Current.CancellationToken);
    }

    internal async Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeInNewScopeAsync(UserId actor, ApiKeyId id)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IApiKeyStore>().RevokeAsync(new(actor, ApiKeyOwnerKind.User, null, id), TestContext.Current.CancellationToken);
    }

    internal async Task<int> RequestCountAsync(byte[] hash) => (await QuotaStateAsync(hash)).RequestCount;

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

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
        await _postgres.DropDatabaseAsync(_databaseName, TestContext.Current.CancellationToken);
    }
}

internal sealed class FixedApiKeyTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
