using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Api.Tests.Infrastructure;
using Template.Application.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class AuthPersistenceTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private string _connectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        (_databaseName, _connectionString) = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InitialMigrationCreatesExpectedAuthSchema()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'auth'
            ORDER BY table_name
            """;

        await using var reader = await command.ExecuteReaderAsync(
            TestContext.Current.CancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Contains("users", tables);
        Assert.Contains("sessions", tables);
        Assert.Contains("user_claims", tables);
        Assert.Contains("user_logins", tables);
        Assert.Contains("user_tokens", tables);
        Assert.DoesNotContain("roles", tables);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeletingUserCascadesPersistentSessions()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var now = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "local-agent+cascade@local-agent.test",
            NormalizedUserName = "LOCAL-AGENT+CASCADE@LOCAL-AGENT.TEST",
            Email = "local-agent+cascade@local-agent.test",
            NormalizedEmail = "LOCAL-AGENT+CASCADE@LOCAL-AGENT.TEST",
            DisplayName = "Cascade User",
            IsLocalAutomation = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        var session = new AuthSessionEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TicketKeyHash = new byte[32],
            ProtectedTicket = [1, 2, 3],
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(7),
            AuthenticationMethod = BrowserAuthenticationMethods.Local
        };
        db.Users.Add(user);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Users.Remove(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.False(await db.Sessions.AnyAsync(
            row => row.Id == session.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledCallbackFailurePreservesOriginalExceptionAndClearsTrackedChanges()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var unitOfWork = new EfApplicationUnitOfWork(db);
        using var cancellation = new CancellationTokenSource();
        var original = new InvalidOperationException("callback failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unitOfWork.ExecuteAsync<int>(_ =>
            {
                db.Users.Add(new ApplicationUser
                {
                    Id = Guid.CreateVersion7(),
                    UserName = "local-agent+rollback@local-agent.test",
                    NormalizedUserName = "LOCAL-AGENT+ROLLBACK@LOCAL-AGENT.TEST",
                    Email = "local-agent+rollback@local-agent.test",
                    NormalizedEmail = "LOCAL-AGENT+ROLLBACK@LOCAL-AGENT.TEST",
                    DisplayName = "Rollback User",
                    IsLocalAutomation = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                cancellation.Cancel();
                return Task.FromException<int>(original);
            }, cancellation.Token));

        Assert.Same(original, exception);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    private TemplateDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new TemplateDbContext(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseName.Length > 0)
        {
            await postgres.DropDatabaseAsync(
                _databaseName,
                TestContext.Current.CancellationToken);
        }
    }
}
