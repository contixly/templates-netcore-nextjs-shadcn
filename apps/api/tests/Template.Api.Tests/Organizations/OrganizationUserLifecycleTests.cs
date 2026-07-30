using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Api.Tests.Infrastructure;
using Template.Application.Accounts;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationUserLifecycleTests(
    PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Sole_owner_of_shared_organization_must_transfer_ownership()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("owner@local-agent.test");
        var member = await fixture.CreateUserAsync("member@local-agent.test");
        var organizationId = await fixture.CreateOrganizationAsync(owner, member);

        var result = await fixture.DeleteAccountAsync(owner);

        Assert.Equal(
            AccountFailure.OrganizationOwnershipTransferRequired,
            result.Failure);
        Assert.True(await fixture.UserExistsAsync(owner));
        Assert.True(await fixture.OrganizationExistsAsync(organizationId));
        Assert.Equal(2, await fixture.CountMembersAsync(organizationId));
    }

    [Fact]
    public async Task Only_member_organization_is_deleted_with_the_account()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("owner@local-agent.test");
        var organizationId = await fixture.CreateOrganizationAsync(owner);

        var result = await fixture.DeleteAccountAsync(owner);

        Assert.True(result.Succeeded);
        Assert.False(await fixture.UserExistsAsync(owner));
        Assert.False(await fixture.OrganizationExistsAsync(organizationId));
    }

    [Fact]
    public async Task Failed_transfer_precondition_changes_nothing()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("owner@local-agent.test");
        var member = await fixture.CreateUserAsync("member@local-agent.test");
        var organizationId = await fixture.CreateOrganizationAsync(owner, member);
        var soleMemberOrganizationId = await fixture.CreateOrganizationAsync(
            owner,
            name: "Must Not Be Partially Deleted");
        await fixture.CreateSessionAsync(owner, organizationId);

        var result = await fixture.DeleteAccountAsync(owner);

        Assert.Equal(
            AccountFailure.OrganizationOwnershipTransferRequired,
            result.Failure);
        Assert.True(await fixture.UserExistsAsync(owner));
        Assert.True(await fixture.OrganizationExistsAsync(organizationId));
        Assert.True(await fixture.OrganizationExistsAsync(
            soleMemberOrganizationId));
        Assert.Equal(2, await fixture.CountMembersAsync(organizationId));
        Assert.Equal(
            1,
            await fixture.CountMembersAsync(soleMemberOrganizationId));
        Assert.Equal(1, await fixture.CountSessionsAsync(owner));
    }

    [Fact]
    public async Task Account_deletion_removes_membership_when_another_owner_remains()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var firstOwner = await fixture.CreateUserAsync(
            "first-owner@local-agent.test");
        var secondOwner = await fixture.CreateUserAsync(
            "second-owner@local-agent.test");
        var organizationId = await fixture.CreateOrganizationAsync(
            firstOwner,
            secondOwner,
            secondMemberIsOwner: true);

        var result = await fixture.DeleteAccountAsync(firstOwner);

        Assert.True(result.Succeeded);
        Assert.False(await fixture.UserExistsAsync(firstOwner));
        Assert.True(await fixture.OrganizationExistsAsync(organizationId));
        Assert.Equal(1, await fixture.CountMembersAsync(organizationId));
        Assert.Equal(1, await fixture.CountOwnersAsync(organizationId));
    }

    [Fact]
    public async Task Local_cleanup_reports_deleted_sole_member_organizations()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync(
            "local-agent+owner@local-agent.test");
        var first = await fixture.CreateOrganizationAsync(owner);
        var second = await fixture.CreateOrganizationAsync(
            owner,
            name: "Second Organization");
        await fixture.SetCurrentSessionAsync(owner);

        var result = await fixture.CleanupAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.DeletedOrganizations);
        Assert.False(await fixture.UserExistsAsync(owner));
        Assert.False(await fixture.OrganizationExistsAsync(first));
        Assert.False(await fixture.OrganizationExistsAsync(second));
    }

    [Fact]
    public async Task Local_cleanup_of_plain_user_reports_zero_organizations()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var user = await fixture.CreateUserAsync(
            "local-agent+plain@local-agent.test");
        await fixture.SetCurrentSessionAsync(user);

        var result = await fixture.CleanupAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.DeletedOrganizations);
        Assert.False(await fixture.UserExistsAsync(user));
    }

    [Fact]
    public async Task Transfer_required_local_cleanup_is_atomic()
    {
        await using var fixture =
            await OrganizationUserLifecycleFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync(
            "local-agent+owner@local-agent.test");
        var member = await fixture.CreateUserAsync(
            "local-agent+member@local-agent.test");
        var organizationId = await fixture.CreateOrganizationAsync(owner, member);
        await fixture.CreateSessionAsync(owner, organizationId);
        await fixture.SetCurrentSessionAsync(owner);

        var result = await fixture.CleanupAsync();

        Assert.Equal(
            AuthFailure.OrganizationOwnershipTransferRequired,
            result.Failure);
        Assert.True(await fixture.UserExistsAsync(owner));
        Assert.True(await fixture.OrganizationExistsAsync(organizationId));
        Assert.Equal(2, await fixture.CountMembersAsync(organizationId));
        Assert.Equal(1, await fixture.CountSessionsAsync(owner));
    }
}

internal sealed class OrganizationUserLifecycleFixture : IAsyncDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainerFixture _postgres;
    private readonly string _databaseName;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;
    private readonly LifecycleBrowserSessionGateway _sessions;
    private readonly Dictionary<UserId, AuthUser> _users = [];

    private OrganizationUserLifecycleFixture(
        PostgreSqlContainerFixture postgres,
        string databaseName,
        string connectionString,
        ServiceProvider services,
        LifecycleBrowserSessionGateway sessions)
    {
        _postgres = postgres;
        _databaseName = databaseName;
        _connectionString = connectionString;
        _services = services;
        _sessions = sessions;
    }

    internal static async Task<OrganizationUserLifecycleFixture> CreateAsync(
        PostgreSqlContainerFixture postgres)
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString,
                ["DataProtection:ApplicationName"] = "Template"
            })
            .Build();
        var sessions = new LifecycleBrowserSessionGateway();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(
            new LifecycleTimeProvider(Now));
        services.AddAuthentication();
        services.AddAuthInfrastructure(
            configuration,
            new TestHostEnvironment());
        services.RemoveAll<IBrowserSessionGateway>();
        services.AddSingleton<IBrowserSessionGateway>(sessions);
        services.AddScoped<AccountService>();
        services.AddScoped<LocalAutomationAuthService>();
        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new OrganizationUserLifecycleFixture(
            postgres,
            database.DatabaseName,
            database.ConnectionString,
            provider,
            sessions);
    }

    internal async Task<UserId> CreateUserAsync(string email)
    {
        await using var scope = _services.CreateAsyncScope();
        var user = await scope.ServiceProvider
            .GetRequiredService<ILocalIdentityGateway>()
            .CreateLocalAsync(
                new LocalAutomationCredentials(
                    email,
                    email,
                    "local-lifecycle-password"),
                TestContext.Current.CancellationToken);
        _users.Add(user.Id, user);
        return user.Id;
    }

    internal async Task<OrganizationId> CreateOrganizationAsync(
        UserId owner,
        UserId? secondMember = null,
        bool secondMemberIsOwner = false,
        string name = "Lifecycle Organization")
    {
        await using var db = CreateDbContext();
        var organizationId = OrganizationId.New();
        db.Organizations.Add(new OrganizationEntity
        {
            Id = organizationId.Value,
            Name = name,
            Slug = $"lifecycle-{Guid.NewGuid():N}",
            CreatedAt = Now,
            UpdatedAt = Now
        });
        db.OrganizationMembers.Add(new OrganizationMemberEntity
        {
            Id = OrganizationMemberId.New().Value,
            OrganizationId = organizationId.Value,
            UserId = owner.Value,
            Role = OrganizationRole.Owner.Value,
            JoinedAt = Now,
            UpdatedAt = Now
        });
        if (secondMember is not null)
        {
            db.OrganizationMembers.Add(new OrganizationMemberEntity
            {
                Id = OrganizationMemberId.New().Value,
                OrganizationId = organizationId.Value,
                UserId = secondMember.Value.Value,
                Role = secondMemberIsOwner
                    ? OrganizationRole.Owner.Value
                    : OrganizationRole.Member.Value,
                JoinedAt = Now,
                UpdatedAt = Now
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return organizationId;
    }

    internal async Task CreateSessionAsync(
        UserId userId,
        OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        db.Sessions.Add(new AuthSessionEntity
        {
            Id = SessionId.New().Value,
            UserId = userId.Value,
            ActiveOrganizationId = organizationId.Value,
            TicketKeyHash = Guid.NewGuid().ToByteArray(),
            ProtectedTicket = [1, 2, 3],
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = Now.AddDays(7),
            AuthenticationMethod = BrowserAuthenticationMethods.Local
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal Task SetCurrentSessionAsync(UserId userId)
    {
        _sessions.Current = new AuthenticatedSession(
            _users[userId],
            new BrowserSession(
                SessionId.New(),
                Now,
                Now,
                Now.AddDays(7),
                BrowserAuthenticationMethods.Local));
        return Task.CompletedTask;
    }

    internal async Task<AccountOperationResult<AccountDeletion>>
        DeleteAccountAsync(UserId userId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AccountService>()
            .DeleteAsync(
                userId,
                _users[userId].Email,
                TestContext.Current.CancellationToken);
    }

    internal async Task<AuthOperationResult<LocalAutomationCleanup>> CleanupAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<LocalAutomationAuthService>()
            .CleanupAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<bool> UserExistsAsync(UserId userId)
    {
        await using var db = CreateDbContext();
        return await db.Users.AnyAsync(
            row => row.Id == userId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<bool> OrganizationExistsAsync(
        OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        return await db.Organizations.AnyAsync(
            row => row.Id == organizationId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountMembersAsync(OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.CountAsync(
            row => row.OrganizationId == organizationId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountOwnersAsync(OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.CountAsync(
            row =>
                row.OrganizationId == organizationId.Value &&
                row.Role == OrganizationRole.Owner.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountSessionsAsync(UserId userId)
    {
        await using var db = CreateDbContext();
        return await db.Sessions.CountAsync(
            row => row.UserId == userId.Value,
            TestContext.Current.CancellationToken);
    }

    private TemplateDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new TemplateDbContext(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DropDatabaseAsync(
            _databaseName,
            CancellationToken.None);
    }

    private sealed class LifecycleBrowserSessionGateway
        : IBrowserSessionGateway
    {
        public AuthenticatedSession? Current { get; set; }

        public Task<AuthenticatedSession?> GetCurrentAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<BrowserSession> SignInAsync(
            AuthUser user,
            string authenticationMethod,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sign-in is not under test.");

        public Task<BrowserSession> RenewCurrentAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Renewal is not under test.");

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            Current = null;
            return Task.CompletedTask;
        }
    }

    private sealed class LifecycleTimeProvider(DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
