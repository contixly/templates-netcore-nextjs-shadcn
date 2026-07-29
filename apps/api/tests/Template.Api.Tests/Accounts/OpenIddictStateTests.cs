using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.EntityFrameworkCore.Models;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Api.Tests.Accounts;

public sealed class OpenIddictStateTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _timeProvider = new(Now);
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
        services.AddOptions();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddAuthInfrastructure(
            configuration,
            new TestHostEnvironment());
        _services = services.BuildServiceProvider(validateScopes: true);

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await postgres.DropDatabaseAsync(
            _databaseName,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RedeemedPersistentStateTokenIsInvalidForReplay()
    {
        await using var scope = _services.CreateAsyncScope();
        var manager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictTokenManager>();
        var token = await manager.CreateAsync(
            new OpenIddictTokenDescriptor
            {
                CreationDate = Now,
                ExpirationDate = Now.AddMinutes(15),
                Payload = "protected-state-payload",
                Status = Statuses.Valid,
                Type = TokenTypeIdentifiers.Private.StateToken
            },
            TestContext.Current.CancellationToken);

        Assert.True(await manager.TryRedeemAsync(
            token,
            TestContext.Current.CancellationToken));
        Assert.True(await manager.HasStatusAsync(
            token,
            Statuses.Redeemed,
            TestContext.Current.CancellationToken));
        Assert.False(await manager.HasStatusAsync(
            token,
            Statuses.Valid,
            TestContext.Current.CancellationToken));

        var replay = new OpenIddictClientEvents.ValidateTokenContext(
            new OpenIddictClientTransaction
            {
                Logger = NullLogger.Instance
            })
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity()),
            TokenId = await manager.GetIdAsync(
                token,
                TestContext.Current.CancellationToken)
        };
        await new OpenIddictClientHandlers.Protection.ValidateTokenEntry(manager)
            .HandleAsync(replay);
        Assert.True(replay.IsRejected);
        Assert.Equal(
            OpenIddictConstants.Errors.InvalidToken,
            replay.Error);

        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.OpenIddictTokens.AsNoTracking().SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(Statuses.Redeemed, row.Status);
        Assert.Equal(TokenTypeIdentifiers.Private.StateToken, row.Type);
        Assert.NotNull(row.RedemptionDate);
    }

    [Fact]
    public async Task CleanupDeletesAtMostFiveHundredEligibleStateRowsPerPass()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.OpenIddictTokens.AddRange(
                Enumerable.Range(0, 501).Select(index =>
                    Token(
                        $"expired-{index:D3}",
                        Statuses.Valid,
                        expiration: Now.AddMinutes(-1))));
            db.OpenIddictTokens.Add(
                Token(
                    "redeemed-old",
                    Statuses.Redeemed,
                    expiration: Now.AddDays(1),
                    redemption: Now.AddHours(-25)));
            db.OpenIddictTokens.Add(
                Token(
                    "redeemed-recent",
                    Statuses.Redeemed,
                    expiration: Now.AddDays(1),
                    redemption: Now.AddHours(-23)));
            db.OpenIddictTokens.Add(
                Token(
                    "revoked-old",
                    Statuses.Revoked,
                    expiration: Now.AddDays(1),
                    redemption: Now.AddHours(-25)));
            db.OpenIddictTokens.Add(
                Token(
                    "valid-future",
                    Statuses.Valid,
                    expiration: Now.AddDays(1)));
            db.OpenIddictTokens.Add(new OpenIddictEntityFrameworkCoreToken
            {
                Id = "expired-access-token",
                CreationDate = Now.AddDays(-1).UtcDateTime,
                ExpirationDate = Now.AddMinutes(-1).UtcDateTime,
                Status = Statuses.Valid,
                Type = TokenTypeIdentifiers.AccessToken
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var cleanup = _services
            .GetRequiredService<OpenIddictStateCleanupService>();
        var firstPass = await cleanup.CleanupOnceAsync(
            TestContext.Current.CancellationToken);
        var secondPass = await cleanup.CleanupOnceAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(500, firstPass);
        Assert.Equal(2, secondPass);

        await using var verificationScope = _services.CreateAsyncScope();
        var remaining = await verificationScope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .OpenIddictTokens
            .AsNoTracking()
            .OrderBy(token => token.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                "expired-access-token",
                "redeemed-recent",
                "revoked-old",
                "valid-future"
            ],
            remaining.Select(token => token.Id));
    }

    private static OpenIddictEntityFrameworkCoreToken Token(
        string id,
        string status,
        DateTimeOffset expiration,
        DateTimeOffset? redemption = null) =>
        new()
        {
            Id = id,
            CreationDate = Now.AddDays(-2).UtcDateTime,
            ExpirationDate = expiration.UtcDateTime,
            RedemptionDate = redemption?.UtcDateTime,
            Status = status,
            Type = TokenTypeIdentifiers.Private.StateToken
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
