using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Api.Tests.Infrastructure;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class IdentityGatewayTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        _databaseName = database.DatabaseName;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddAuthentication();
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void GeneratorProducesReservedEmailAndThirtyTwoRandomPasswordBytes()
    {
        var generator = _services
            .GetRequiredService<ILocalAutomationCredentialGenerator>();

        var first = generator.Generate();
        var second = generator.Generate();
        var passwordHex = first.Password["local-".Length..];

        Assert.True(LocalAutomationCredentialPolicy.IsLocalEmail(first.Email));
        Assert.StartsWith("Local Automation ", first.Name);
        Assert.Equal(64, passwordHex.Length);
        Assert.Equal(32, Convert.FromHexString(passwordHex).Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task CreatePersistsPrimaryEmailForAccountAndOwnershipProjection()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        var externalAccounts = scope.ServiceProvider
            .GetRequiredService<IExternalAccountStore>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var credentials = new LocalAutomationCredentials(
            "Local Account",
            "local-agent+account@local-agent.test",
            "local-account-password");

        var created = await gateway.CreateLocalAsync(
            credentials,
            TestContext.Current.CancellationToken);

        var user = await db.Users.AsNoTracking().SingleAsync(
            row => row.Id == created.Id.Value,
            TestContext.Current.CancellationToken);
        var primary = await db.UserEmails.AsNoTracking().SingleAsync(
            row => row.UserId == created.Id.Value,
            TestContext.Current.CancellationToken);
        var account = await accounts.GetAsync(
            new UserId(created.Id.Value),
            TestContext.Current.CancellationToken);
        var owner = await externalAccounts.FindUserByEmailAsync(
            credentials.Email.ToUpperInvariant(),
            TestContext.Current.CancellationToken);

        Assert.True(primary.IsPrimary);
        Assert.Equal(user.Email, primary.Email);
        Assert.Equal(user.NormalizedEmail, primary.NormalizedEmail);
        Assert.Equal(primary.NormalizedEmail, account?.PrimaryEmail.NormalizedValue);
        Assert.Equal(created.Id, owner?.Id);
    }

    [Fact]
    public async Task CreateHashesPasswordAndMarksUnverifiedLocalUser()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var credentials = new LocalAutomationCredentials(
            "Local Identity",
            "local-agent+identity@local-agent.test",
            "local-identity-password");

        var created = await gateway.CreateLocalAsync(
            credentials,
            TestContext.Current.CancellationToken);
        var row = await db.Users.SingleAsync(
            user => user.Id == created.Id.Value,
            TestContext.Current.CancellationToken);

        Assert.True(row.IsLocalAutomation);
        Assert.False(row.EmailConfirmed);
        Assert.NotNull(row.PasswordHash);
        Assert.DoesNotContain(credentials.Password, row.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(credentials.Email, row.UserName);
        Assert.Equal(credentials.Email, row.Email);
    }

    [Fact]
    public async Task ConfirmEmailMarksOnlyTheRequestedLocalIdentityVerified()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var requested = await gateway.CreateLocalAsync(
            new LocalAutomationCredentials(
                "Confirmation Target",
                "local-agent+confirmation-target@local-agent.test",
                "local-confirmation-target-password"),
            TestContext.Current.CancellationToken);
        var untouched = await gateway.CreateLocalAsync(
            new LocalAutomationCredentials(
                "Confirmation Untouched",
                "local-agent+confirmation-untouched@local-agent.test",
                "local-confirmation-untouched-password"),
            TestContext.Current.CancellationToken);

        var confirmed = await gateway.ConfirmEmailAsync(
            requested.Id,
            TestContext.Current.CancellationToken);

        Assert.True(confirmed.EmailVerified);
        Assert.True(await db.Users
            .Where(user => user.Id == requested.Id.Value)
            .Select(user => user.EmailConfirmed)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Users
            .Where(user => user.Id == untouched.Id.Value)
            .Select(user => user.EmailConfirmed)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateNormalizedEmailUsesStableDuplicateException()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var first = new LocalAutomationCredentials(
            "First",
            "local-agent+duplicate@local-agent.test",
            "local-duplicate-password");
        var second = first with
        {
            Name = "Second",
            Email = "LOCAL-AGENT+DUPLICATE@LOCAL-AGENT.TEST"
        };
        await gateway.CreateLocalAsync(first, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<DuplicateLocalIdentityException>(
            () => gateway.CreateLocalAsync(
                second,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FifthBadPasswordLocksUserAndNeverReturnsIdentity()
    {
        await using var scope = _services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ILocalIdentityGateway>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var credentials = new LocalAutomationCredentials(
            "Locked User",
            "local-agent+locked@local-agent.test",
            "local-correct-password");
        var created = await gateway.CreateLocalAsync(
            credentials,
            TestContext.Current.CancellationToken);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Null(await gateway.CheckLocalPasswordAsync(
                credentials.Email,
                "local-wrong-password",
                TestContext.Current.CancellationToken));
        }

        var row = await db.Users.SingleAsync(
            user => user.Id == created.Id.Value,
            TestContext.Current.CancellationToken);
        Assert.NotNull(row.LockoutEnd);
        Assert.True(row.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.Null(await gateway.CheckLocalPasswordAsync(
            credentials.Email,
            credentials.Password,
            TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                TestContext.Current.CancellationToken);
        }
    }
}
