using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class PostgresTicketStoreTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;
    private Guid _userId;
    private readonly MutableTimeProvider _time = new(
        new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));

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
        services.AddDataProtection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(_time);
        services.AddHttpContextAccessor();
        services.AddAuthentication("TicketStoreTest")
            .AddCookie("TicketStoreTest");
        services.AddAuthInfrastructure(configuration);
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        _userId = Guid.CreateVersion7();
        var now = _time.GetUtcNow();
        db.Users.Add(new ApplicationUser
        {
            Id = _userId,
            UserName = "local-agent+ticket@local-agent.test",
            NormalizedUserName = "LOCAL-AGENT+TICKET@LOCAL-AGENT.TEST",
            Email = "local-agent+ticket@local-agent.test",
            NormalizedEmail = "LOCAL-AGENT+TICKET@LOCAL-AGENT.TEST",
            DisplayName = "Ticket User",
            IsLocalAutomation = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StoreRetrieveRenewAndRemoveUseOnlyHashedLookupKey()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var sessionId = Guid.CreateVersion7();
        var ticket = CreateTicket(sessionId, _time.GetUtcNow().AddDays(7));

        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(sessionId, row.Id);
        Assert.Equal(32, row.TicketKeyHash.Length);
        Assert.DoesNotContain(
            key,
            Convert.ToHexString(row.TicketKeyHash),
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(TicketSerializer.Default.Serialize(ticket), row.ProtectedTicket);

        var retrieved = await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            sessionId.ToString(),
            retrieved!.Principal.FindFirstValue(BrowserSessionClaimTypes.SessionId));

        var renewedExpiry = _time.GetUtcNow().AddDays(8);
        var renewed = CreateTicket(sessionId, renewedExpiry);
        await store.RenewAsync(
            key,
            renewed,
            context,
            TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        Assert.Equal(
            renewedExpiry,
            (await db.Sessions.SingleAsync(TestContext.Current.CancellationToken)).ExpiresAt,
            TimeSpan.FromSeconds(1));

        await store.RemoveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveLazilyDeletesExpiredSession()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var key = await store.StoreAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddMinutes(1)),
            context,
            TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    private DefaultHttpContext CreateHttpContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Headers.UserAgent = "ticket-store-test";
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }

    private AuthenticationTicket CreateTicket(Guid sessionId, DateTimeOffset expiresAt)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                new Claim(BrowserSessionClaimTypes.SessionId, sessionId.ToString())
            ],
            "TicketStoreTest");
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = _time.GetUtcNow(),
                ExpiresUtc = expiresAt,
                AllowRefresh = true
            },
            "TicketStoreTest");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan value) => _utcNow += value;
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
