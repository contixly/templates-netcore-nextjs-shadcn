using System.Buffers.Binary;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Api.Authentication;
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
    private readonly SessionCommandBarrier _commandBarrier = new();
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
        services.AddApiAuthentication();
        services.AddSingleton(_commandBarrier);
        services.AddDbContext<AuthDbContext>((provider, options) =>
        {
            AuthDbContext.Configure(options, database.ConnectionString);
            options.AddInterceptors(
                provider.GetRequiredService<SessionCommandBarrier>());
        });
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
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
    public async Task GatewaySignInPersistsTheIssuedCookieSession()
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

        var issued = await gateway.SignInAsync(
            user,
            BrowserAuthenticationMethods.Local,
            TestContext.Current.CancellationToken);

        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        db.ChangeTracker.Clear();
        var stored = Assert.Single(await db.Sessions
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(issued.Id.Value, stored.Id);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task InitialStoreProtectsExactlyOneCanonicalMethodClaim(
        string ticketShape)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(
            Guid.CreateVersion7(),
            _time.GetUtcNow().AddDays(7),
            AuthenticationMethodsFor(ticketShape));

        await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);

        var row = await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var protectedTicket = UnprotectTicket(
            scope.ServiceProvider,
            row.ProtectedTicket);
        Assert.Equal(BrowserAuthenticationMethods.Local, row.AuthenticationMethod);
        Assert.Equal(
            [BrowserAuthenticationMethods.Local],
            protectedTicket.Principal
                .FindAll(BrowserSessionClaimTypes.AuthenticationMethod)
                .Select(claim => claim.Value));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task RetrieveRejectsLocalProjectedTicketAgainstProviderRow(
        string ticketShape)
    {
        var sessionId = Guid.CreateVersion7();
        var key = await StoreTicketAsync(CreateTicket(
            sessionId,
            _time.GetUtcNow().AddDays(7),
            BrowserAuthenticationMethods.GitHub));
        await ReplaceStoredMethodAndTicketAsync(
            BrowserAuthenticationMethods.GitHub,
            CreateTicket(
                sessionId,
                _time.GetUtcNow().AddDays(7),
                AuthenticationMethodsFor(ticketShape)));
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);

        var retrieved = await scope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RetrieveAsync(
                key,
                context,
                TestContext.Current.CancellationToken);

        Assert.Null(retrieved);
        Assert.True(BrowserSessionCookieInvalidation.IsRequested(context));
        await AssertNoSessionsAsync();
    }

    [Fact]
    public async Task RetrieveRejectsProviderTicketAgainstLocalRow()
    {
        var sessionId = Guid.CreateVersion7();
        var key = await StoreTicketAsync(CreateTicket(
            sessionId,
            _time.GetUtcNow().AddDays(7),
            BrowserAuthenticationMethods.GitHub));
        await ReplaceStoredMethodAsync(BrowserAuthenticationMethods.Local);
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);

        var retrieved = await scope.ServiceProvider
            .GetRequiredService<PostgresTicketStore>()
            .RetrieveAsync(
                key,
                context,
                TestContext.Current.CancellationToken);

        Assert.Null(retrieved);
        Assert.True(BrowserSessionCookieInvalidation.IsRequested(context));
        await AssertNoSessionsAsync();
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
    public async Task RetrieveDeletesSupportedButTruncatedTicketPayload()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7));
        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);
        var serialized = TicketSerializer.Default.Serialize(ticket);
        await MutateStoredTicketAsync(Protect(scope.ServiceProvider, serialized[..^1]));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveLazilyDeletesProtectedV5TicketWithMalformedSerializerData()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7));
        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);
        var malformed = CreateV5PayloadThatThrowsArgumentException(
            TicketSerializer.Default.Serialize(ticket));

        Assert.Equal(5, BinaryPrimitives.ReadInt32LittleEndian(malformed));
        Assert.Throws<ArgumentException>(() =>
            TicketSerializer.Default.Deserialize(malformed));
        await MutateStoredTicketAsync(Protect(scope.ServiceProvider, malformed));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveDeletesTicketWithUnexpectedAuthenticationScheme()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7));
        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);
        var unexpected = new AuthenticationTicket(
            ticket.Principal,
            ticket.Properties,
            "Unexpected.Cookie.Scheme");
        await MutateStoredTicketAsync(Protect(
            scope.ServiceProvider,
            TicketSerializer.Default.Serialize(unexpected)));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveDeletesTicketWhoseClaimsDoNotMatchPersistedRow()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7));
        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);
        var mismatched = CreateTicket(
            Guid.CreateVersion7(),
            _time.GetUtcNow().AddDays(7));
        await MutateStoredTicketAsync(Protect(
            scope.ServiceProvider,
            TicketSerializer.Default.Serialize(mismatched)));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetrieveDeletesTicketWithDuplicateIdentityClaims()
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
        var identity = (ClaimsIdentity)ticket.Principal.Identity!;
        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.SessionId,
            sessionId.ToString()));
        await MutateStoredTicketAsync(Protect(
            scope.ServiceProvider,
            TicketSerializer.Default.Serialize(ticket)));

        Assert.Null(await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken));
        Assert.False(await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentRemoveIsIdempotent()
    {
        var key = await StoreTicketAsync(
            CreateTicket(Guid.CreateVersion7(), _time.GetUtcNow().AddDays(7)));
        _commandBarrier.CoordinateParallelSessionDeletes(2);

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

        await _commandBarrier.ReleaseParallelCommandsAsync(
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
        _commandBarrier.CoordinateParallelSessionDeletes(2);

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

        await _commandBarrier.ReleaseParallelCommandsAsync(
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
        _commandBarrier.CoordinateParallelSessionDeletes(2);

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

        await _commandBarrier.ReleaseParallelCommandsAsync(
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
        _commandBarrier.CoordinateSessionDeleteBeforeUpdate();

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

    private async Task ReplaceStoredMethodAndTicketAsync(
        string authenticationMethod,
        AuthenticationTicket ticket)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);
        row.AuthenticationMethod = authenticationMethod;
        row.ProtectedTicket = Protect(
            scope.ServiceProvider,
            TicketSerializer.Default.Serialize(ticket));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ReplaceStoredMethodAsync(string authenticationMethod)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);
        row.AuthenticationMethod = authenticationMethod;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static byte[] Protect(
        IServiceProvider services,
        byte[] serializedTicket) =>
        services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(
                "Template.Infrastructure.Authentication.PostgresTicketStore.v1")
            .Protect(serializedTicket);

    private static AuthenticationTicket UnprotectTicket(
        IServiceProvider services,
        byte[] protectedTicket) =>
        TicketSerializer.Default.Deserialize(
            services
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(
                    "Template.Infrastructure.Authentication.PostgresTicketStore.v1")
                .Unprotect(protectedTicket)) ??
        throw new InvalidOperationException("Stored ticket could not be deserialized.");

    private static byte[] CreateV5PayloadThatThrowsArgumentException(
        byte[] serializedTicket)
    {
        for (var index = sizeof(int); index < serializedTicket.Length; index++)
        {
            for (var value = byte.MinValue; value <= byte.MaxValue; value++)
            {
                if (value == serializedTicket[index])
                {
                    continue;
                }

                var malformed = (byte[])serializedTicket.Clone();
                malformed[index] = (byte)value;
                try
                {
                    _ = TicketSerializer.Default.Deserialize(malformed);
                }
                catch (ArgumentException)
                {
                    return malformed;
                }
                catch (IOException)
                {
                    // Keep searching for the serializer format exception under test.
                }
            }
        }

        throw new InvalidOperationException(
            "Unable to create malformed TicketSerializer v5 data that throws ArgumentException.");
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

    private AuthenticationTicket CreateTicket(
        Guid sessionId,
        DateTimeOffset expiresAt,
        params string[] authenticationMethods)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                new Claim(BrowserSessionClaimTypes.SessionId, sessionId.ToString())
            ],
            ApiAuthenticationDefaults.SchemeName);
        foreach (var authenticationMethod in authenticationMethods)
        {
            identity.AddClaim(new Claim(
                BrowserSessionClaimTypes.AuthenticationMethod,
                authenticationMethod));
        }

        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = _time.GetUtcNow(),
                ExpiresUtc = expiresAt,
                AllowRefresh = true
            },
            ApiAuthenticationDefaults.SchemeName);
    }

    private static string[] AuthenticationMethodsFor(string ticketShape) =>
        ticketShape switch
        {
            "missing" => [],
            "duplicate" =>
            [
                BrowserAuthenticationMethods.GitHub,
                BrowserAuthenticationMethods.Google
            ],
            "unknown" => ["linkedin"],
            _ => throw new ArgumentOutOfRangeException(nameof(ticketShape))
        };

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
