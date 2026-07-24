using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Api.Tests.Infrastructure;
using Template.Domain.Authentication;
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
    private readonly SessionWriteBarrier _writeBarrier = new();
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
        services.AddSingleton(_writeBarrier);
        services.AddDbContext<AuthDbContext>((provider, options) =>
            options.AddInterceptors(
                provider.GetRequiredService<SessionWriteBarrier>()));
        services
            .AddOptions<CookieAuthenticationOptions>("TicketStoreTest")
            .Configure<PostgresTicketStore>((options, store) =>
                options.SessionStore = store);
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
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
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

    [Fact]
    public async Task RepeatedGatewaySignInReplacesTheExistingCookieSession()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var gateway = scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>();
        var user = new AuthUser(
            new UserId(_userId),
            "Ticket User",
            "local-agent+ticket@local-agent.test",
            false,
            null,
            true);

        var first = await gateway.SignInAsync(
            user,
            TestContext.Current.CancellationToken);
        var second = await gateway.SignInAsync(
            user,
            TestContext.Current.CancellationToken);

        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.ChangeTracker.Clear();
        var stored = Assert.Single(await db.Sessions
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id.Value, stored.Id);
    }

    [Fact]
    public async Task RetrieveLazilyDeletesProtectedButIncompatibleTicket()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var key = await store.StoreAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7)),
            context,
            TestContext.Current.CancellationToken);
        var protector = scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(
                "Template.Infrastructure.Authentication.PostgresTicketStore.v1");
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);
        row.ProtectedTicket = protector.Protect(BitConverter.GetBytes(0));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentRemoveIsIdempotent()
    {
        var key = await StoreTicketAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7)));
        _writeBarrier.CoordinateParallelDeletes(2);

        await using var firstScope = _services.CreateAsyncScope();
        await using var secondScope = _services.CreateAsyncScope();
        var first = firstScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RemoveAsync(
                key,
                CreateHttpContext(firstScope.ServiceProvider),
                TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RemoveAsync(
                key,
                CreateHttpContext(secondScope.ServiceProvider),
                TestContext.Current.CancellationToken);

        await _writeBarrier.ReleaseWhenBlockedOrCompletedAsync(
            Task.WhenAll(first, second),
            TestContext.Current.CancellationToken);
        await AssertNoSessionsAsync();
    }

    [Fact]
    public async Task ConcurrentExpiredRetrievalIsIdempotent()
    {
        var key = await StoreTicketAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddMinutes(1)));
        _time.Advance(TimeSpan.FromMinutes(2));
        _writeBarrier.CoordinateParallelDeletes(2);

        await using var firstScope = _services.CreateAsyncScope();
        await using var secondScope = _services.CreateAsyncScope();
        var first = firstScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RetrieveAsync(
                key,
                CreateHttpContext(firstScope.ServiceProvider),
                TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RetrieveAsync(
                key,
                CreateHttpContext(secondScope.ServiceProvider),
                TestContext.Current.CancellationToken);

        await _writeBarrier.ReleaseWhenBlockedOrCompletedAsync(
            Task.WhenAll(first, second),
            TestContext.Current.CancellationToken);
        Assert.Null(await first);
        Assert.Null(await second);
        await AssertNoSessionsAsync();
    }

    [Fact]
    public async Task ConcurrentCorruptRetrievalIsIdempotent()
    {
        var key = await StoreTicketAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7)));
        await MutateStoredTicketAsync([1, 2, 3]);
        _writeBarrier.CoordinateParallelDeletes(2);

        await using var firstScope = _services.CreateAsyncScope();
        await using var secondScope = _services.CreateAsyncScope();
        var first = firstScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RetrieveAsync(
                key,
                CreateHttpContext(firstScope.ServiceProvider),
                TestContext.Current.CancellationToken);
        var second = secondScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RetrieveAsync(
                key,
                CreateHttpContext(secondScope.ServiceProvider),
                TestContext.Current.CancellationToken);

        await _writeBarrier.ReleaseWhenBlockedOrCompletedAsync(
            Task.WhenAll(first, second),
            TestContext.Current.CancellationToken);
        Assert.Null(await first);
        Assert.Null(await second);
        await AssertNoSessionsAsync();
    }

    [Fact]
    public async Task ConcurrentRenewAndRemoveLeaveTheSessionRevoked()
    {
        var sessionId = Guid.CreateVersion7();
        var key = await StoreTicketAsync(
            CreateTicket(sessionId, _time.GetUtcNow().AddDays(7)));
        _writeBarrier.CoordinateDeleteBeforeUpdate();

        await using var removeScope = _services.CreateAsyncScope();
        await using var renewScope = _services.CreateAsyncScope();
        var remove = removeScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RemoveAsync(
                key,
                CreateHttpContext(removeScope.ServiceProvider),
                TestContext.Current.CancellationToken);
        var renew = renewScope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RenewAsync(
                key,
                CreateTicket(sessionId, _time.GetUtcNow().AddDays(8)),
                CreateHttpContext(renewScope.ServiceProvider),
                TestContext.Current.CancellationToken);

        await Task.WhenAll(remove, renew);
        await AssertNoSessionsAsync();
    }

    [Fact]
    public async Task RemoveThenRenewInTheSameContextDoesNotRestoreARevokedSession()
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

        await store.RemoveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
        await store.RenewAsync(
            key,
            ticket,
            context,
            TestContext.Current.CancellationToken);

        await AssertNoSessionsAsync();
    }

    private async Task<string> StoreTicketAsync(AuthenticationTicket ticket)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .StoreAsync(
                ticket,
                CreateHttpContext(scope.ServiceProvider),
                TestContext.Current.CancellationToken);
    }

    private async Task MutateStoredTicketAsync(byte[] protectedTicket)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);
        row.ProtectedTicket = protectedTicket;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task AssertNoSessionsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions
            .AnyAsync(TestContext.Current.CancellationToken));
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

    private sealed class SessionWriteBarrier : SaveChangesInterceptor
    {
        private readonly ConcurrentDictionary<DbContext, EntityState> _writes = new();
        private CoordinationMode _mode;
        private int _participants;
        private int _arrived;
        private TaskCompletionSource _allBlocked = NewSignal();
        private TaskCompletionSource _release = NewSignal();
        private TaskCompletionSource _updateReady = NewSignal();
        private TaskCompletionSource _deleteCompleted = NewSignal();

        internal void CoordinateParallelDeletes(int participants)
        {
            _mode = CoordinationMode.ParallelDeletes;
            _participants = participants;
        }

        internal void CoordinateDeleteBeforeUpdate() =>
            _mode = CoordinationMode.DeleteBeforeUpdate;

        internal async Task ReleaseWhenBlockedOrCompletedAsync(
            Task operations,
            CancellationToken cancellationToken)
        {
            await Task.WhenAny(_allBlocked.Task, operations)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            _release.TrySetResult();
            await operations;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context is null || _mode == CoordinationMode.None)
            {
                return result;
            }

            var state = context.ChangeTracker
                .Entries<AuthSessionEntity>()
                .Select(entry => entry.State)
                .SingleOrDefault(value =>
                    value is EntityState.Deleted or EntityState.Modified);
            if (state == EntityState.Detached)
            {
                return result;
            }

            _writes[context] = state;
            if (_mode == CoordinationMode.ParallelDeletes &&
                state == EntityState.Deleted)
            {
                if (Interlocked.Increment(ref _arrived) == _participants)
                {
                    _allBlocked.TrySetResult();
                }

                await _release.Task.WaitAsync(cancellationToken);
            }
            else if (_mode == CoordinationMode.DeleteBeforeUpdate)
            {
                if (state == EntityState.Modified)
                {
                    _updateReady.TrySetResult();
                    await _deleteCompleted.Task.WaitAsync(cancellationToken);
                }
                else if (state == EntityState.Deleted)
                {
                    await _updateReady.Task.WaitAsync(cancellationToken);
                }
            }

            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            SignalDeleteCompleted(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            SignalDeleteCompleted(eventData.Context);
            return Task.CompletedTask;
        }

        private void SignalDeleteCompleted(DbContext? context)
        {
            if (_mode == CoordinationMode.DeleteBeforeUpdate &&
                context is not null &&
                _writes.TryGetValue(context, out var state) &&
                state == EntityState.Deleted)
            {
                _deleteCompleted.TrySetResult();
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private enum CoordinationMode
        {
            None,
            ParallelDeletes,
            DeleteBeforeUpdate
        }
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
