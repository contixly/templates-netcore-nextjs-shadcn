using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
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
    private string _connectionString = string.Empty;
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        _databaseName = database.DatabaseName;
        _connectionString = database.ConnectionString;
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

    [Fact]
    public async Task CleanupTickLogsTransientFailureAndAllowsTheNextTickToRetry()
    {
        const string secretStateId = "secret-state-id-must-not-be-logged";
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            db.OpenIddictTokens.Add(
                Token(
                    secretStateId,
                    Statuses.Valid,
                    expiration: Now.AddMinutes(-1)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var interceptor = new FailOnceCleanupCommandInterceptor();
        var logs = new CapturedLogProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddDbContext<AuthDbContext>(options =>
        {
            AuthDbContext.Configure(options, _connectionString);
            options.AddInterceptors(interceptor);
        });
        services.AddSingleton<OpenIddictStateCleanupService>();
        await using var retryServices =
            services.BuildServiceProvider(validateScopes: true);
        var cleanup = retryServices
            .GetRequiredService<OpenIddictStateCleanupService>();

        interceptor.FailNextCleanup();
        await cleanup.RunCleanupTickAsync(
            TestContext.Current.CancellationToken);

        await using (var verificationScope = _services.CreateAsyncScope())
        {
            Assert.True(await verificationScope.ServiceProvider
                .GetRequiredService<AuthDbContext>()
                .OpenIddictTokens
                .AnyAsync(
                    token => token.Id == secretStateId,
                    TestContext.Current.CancellationToken));
        }

        await cleanup.RunCleanupTickAsync(
            TestContext.Current.CancellationToken);

        await using (var verificationScope = _services.CreateAsyncScope())
        {
            Assert.False(await verificationScope.ServiceProvider
                .GetRequiredService<AuthDbContext>()
                .OpenIddictTokens
                .AnyAsync(
                    token => token.Id == secretStateId,
                    TestContext.Current.CancellationToken));
        }

        var failure = Assert.Single(logs.Logs, log =>
            log.Category ==
                typeof(OpenIddictStateCleanupService).FullName
            && log.Level == LogLevel.Error);
        Assert.Equal(
            "OpenIddict state cleanup tick failed; the next tick will retry.",
            failure.Message);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.DoesNotContain(
            secretStateId,
            string.Join(
                " | ",
                [
                    failure.Message,
                    .. failure.State.Values.Select(value => value?.ToString()),
                    failure.Exception?.ToString()
                ]),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanupTickPropagatesCancellation()
    {
        var cleanup = _services
            .GetRequiredService<OpenIddictStateCleanupService>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cleanup.RunCleanupTickAsync(cancellation.Token));
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

    private sealed class FailOnceCleanupCommandInterceptor
        : DbCommandInterceptor
    {
        private int _failNextCleanup;

        internal void FailNextCleanup() =>
            Interlocked.Exchange(ref _failNextCleanup, 1);

        public override ValueTask<InterceptionResult<int>>
            NonQueryExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    "openiddict_tokens",
                    StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _failNextCleanup, 0) == 1)
            {
                throw new InvalidOperationException(
                    "Transient PostgreSQL cleanup failure.");
            }

            return base.NonQueryExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}
