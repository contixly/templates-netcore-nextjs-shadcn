using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Application.Accounts;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Accounts;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Accounts;

public sealed class AccountSessionPersistenceTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _time = new(Now);
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;
    private Guid _userId;

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
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        _userId = Guid.CreateVersion7();
        db.Users.Add(CreateUser(_userId, "session-owner@example.test"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await postgres.DropDatabaseAsync(
            _databaseName,
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("google")]
    [InlineData("github")]
    [InlineData("gitlab")]
    [InlineData("vk")]
    [InlineData("yandex")]
    public async Task SignInRoundTripsExactlyOneAllowedAuthenticationMethod(
        string authenticationMethod)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var gateway = scope.ServiceProvider.GetRequiredService<IBrowserSessionGateway>();

        var issued = await gateway.SignInAsync(
            CreateAuthUser(),
            authenticationMethod,
            TestContext.Current.CancellationToken);
        var cookie = AssertSessionCookie(context);
        var authenticated = await AuthenticateAsync(cookie);

        Assert.Equal(authenticationMethod, issued.AuthenticationMethod);
        Assert.Equal(
            [authenticationMethod],
            authenticated.Principal!.FindAll(
                    BrowserSessionClaimTypes.AuthenticationMethod)
                .Select(claim => claim.Value));
    }

    [Fact]
    public async Task SignInRejectsAnUnboundedAuthenticationMethod()
    {
        await using var scope = _services.CreateAsyncScope();
        CreateHttpContext(scope.ServiceProvider);
        var gateway = scope.ServiceProvider.GetRequiredService<IBrowserSessionGateway>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            gateway.SignInAsync(
                CreateAuthUser(),
                "linkedin",
                TestContext.Current.CancellationToken));

        Assert.False(await scope.ServiceProvider
            .GetRequiredService<TemplateDbContext>()
            .Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("linkedin")]
    [InlineData("GITHUB")]
    public async Task LegacyOrInvalidTicketProjectsAsLocal(string? storedMethod)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(
            Guid.CreateVersion7(),
            Now.AddDays(7),
            storedMethod is null ? [] : [storedMethod]);
        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);

        var retrieved = await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
        context.User = retrieved!.Principal;
        var current = await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["local"],
            retrieved.Principal.FindAll(BrowserSessionClaimTypes.AuthenticationMethod)
                .Select(claim => claim.Value));
        Assert.Equal("local", current!.Session.AuthenticationMethod);
    }

    [Fact]
    public async Task DuplicateAuthenticationMethodClaimsProjectAsLocal()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var store = scope.ServiceProvider.GetRequiredService<PostgresTicketStore>();
        var ticket = CreateTicket(
            Guid.CreateVersion7(),
            Now.AddDays(7),
            ["github", "google"]);
        var key = await store.StoreAsync(
            ticket,
            context,
            TestContext.Current.CancellationToken);

        var retrieved = await store.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["local"],
            retrieved!.Principal
                .FindAll(BrowserSessionClaimTypes.AuthenticationMethod)
                .Select(claim => claim.Value));
    }

    [Fact]
    public async Task RenewCurrentPreservesProviderAndSessionId()
    {
        var originalCookie = await IssueCookieAsync("github");
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, originalCookie);
        var authentication = await context.AuthenticateAsync(
            ApiAuthenticationDefaults.SchemeName);
        Assert.True(authentication.Succeeded);
        context.User = authentication.Principal!;
        var gateway = scope.ServiceProvider.GetRequiredService<IBrowserSessionGateway>();
        var before = await gateway.GetCurrentAsync(TestContext.Current.CancellationToken);

        var renewed = await gateway.RenewCurrentAsync(
            TestContext.Current.CancellationToken);
        var renewedCookie = AssertSessionCookie(context);
        var renewedAuthentication = await AuthenticateAsync(renewedCookie);

        Assert.Equal(before!.Session.Id, renewed.Id);
        Assert.Equal("github", renewed.AuthenticationMethod);
        Assert.Equal(
            ["github"],
            renewedAuthentication.Principal!
                .FindAll(BrowserSessionClaimTypes.AuthenticationMethod)
                .Select(claim => claim.Value));
        var rows = await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .Sessions.AsNoTracking()
            .Where(row => row.UserId == _userId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(renewed.Id.Value, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task ListingUsesSafeMetadataWithoutDeserializingTickets()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var session = CreateSession(
            _userId,
            Now,
            "github",
            protectedTicket: [0xDE, 0xAD, 0xBE, 0xEF],
            discriminator: 1);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountSessionStore(db, _time);

        var page = await store.ListAsync(
            new UserId(_userId),
            cursor: null,
            limit: 20,
            TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(page);

        var item = Assert.Single(page.Items);
        Assert.Equal("github", item.AuthenticationMethod);
        Assert.DoesNotContain(
            Convert.ToHexString(session.ProtectedTicket),
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Convert.ToHexString(session.TicketKeyHash),
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedTicket", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ticketKeyHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListingOmitsExpiredSessions()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        db.Sessions.AddRange(
            CreateSession(_userId, Now, "google", [1], 1),
            CreateSession(
                _userId,
                Now.AddDays(-8),
                "github",
                [2],
                2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var store = new EfAccountSessionStore(db, _time);

        var page = await store.ListAsync(
            new UserId(_userId),
            cursor: null,
            limit: 20,
            TestContext.Current.CancellationToken);

        Assert.Equal(["google"], page.Items.Select(item => item.AuthenticationMethod));
    }

    [Fact]
    public async Task SingleRevokeIsOwnershipQualified()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var foreignUserId = Guid.CreateVersion7();
        db.Users.Add(CreateUser(foreignUserId, "foreign-session@example.test"));
        var foreign = CreateSession(foreignUserId, Now, "google", [1], 1);
        db.Sessions.Add(foreign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new AccountSessionService(new EfAccountSessionStore(db, _time));

        var result = await service.RevokeAsync(
            new UserId(_userId),
            new SessionId(foreign.Id),
            SessionId.New(),
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountFailure.SessionNotFound, result.Failure);
        Assert.True(await db.Sessions.AnyAsync(
            row => row.Id == foreign.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CurrentSessionRevokeIsRejectedBeforeDelete()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var current = CreateSession(_userId, Now, "github", [1], 1);
        db.Sessions.Add(current);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new AccountSessionService(new EfAccountSessionStore(db, _time));
        var currentId = new SessionId(current.Id);

        var result = await service.RevokeAsync(
            new UserId(_userId),
            currentId,
            currentId,
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountFailure.CurrentSessionCannotBeRevoked, result.Failure);
        Assert.True(await db.Sessions.AnyAsync(
            row => row.Id == current.Id,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RevokeOthersReturnsDeletedCountIncludingExpiredSessions()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var foreignUserId = Guid.CreateVersion7();
        db.Users.Add(CreateUser(foreignUserId, "other-owner@example.test"));
        var current = CreateSession(_userId, Now, "github", [1], 1);
        var other = CreateSession(_userId, Now.AddMinutes(-1), "google", [2], 2);
        var expired = CreateSession(
            _userId,
            Now.AddDays(-8),
            "gitlab",
            [3],
            3);
        var foreign = CreateSession(foreignUserId, Now, "vk", [4], 4);
        db.Sessions.AddRange(current, other, expired, foreign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new AccountSessionService(new EfAccountSessionStore(db, _time));

        var count = await service.RevokeOthersAsync(
            new UserId(_userId),
            new SessionId(current.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
        Assert.True(await db.Sessions.AnyAsync(
            row => row.Id == current.Id,
            TestContext.Current.CancellationToken));
        Assert.True(await db.Sessions.AnyAsync(
            row => row.Id == foreign.Id,
            TestContext.Current.CancellationToken));
    }

    private async Task<string> IssueCookieAsync(string authenticationMethod)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IBrowserSessionGateway>()
            .SignInAsync(
                CreateAuthUser(),
                authenticationMethod,
                TestContext.Current.CancellationToken);
        return AssertSessionCookie(context);
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string cookie)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, cookie);
        return await context.AuthenticateAsync(ApiAuthenticationDefaults.SchemeName);
    }

    private AuthUser CreateAuthUser() =>
        new(
            new UserId(_userId),
            "Session Owner",
            "session-owner@example.test",
            true,
            null,
            false);

    private static ApplicationUser CreateUser(Guid id, string email) =>
        new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            DisplayName = email.Split('@')[0],
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAt = Now,
            UpdatedAt = Now
        };

    private static AuthSessionEntity CreateSession(
        Guid userId,
        DateTimeOffset updatedAt,
        string authenticationMethod,
        byte[] protectedTicket,
        byte discriminator) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TicketKeyHash = Enumerable.Repeat(discriminator, 32).ToArray(),
            ProtectedTicket = protectedTicket,
            CreatedAt = updatedAt.AddMinutes(-1),
            UpdatedAt = updatedAt,
            ExpiresAt = updatedAt.AddDays(7),
            AuthenticationMethod = authenticationMethod,
            IpAddress = IPAddress.Parse($"192.0.2.{discriminator}"),
            UserAgent = $"agent-{discriminator}"
        };

    private AuthenticationTicket CreateTicket(
        Guid sessionId,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> authenticationMethods)
    {
        var identity = new ClaimsIdentity(
            authenticationType: BrowserSessionAuthenticationDefaults.PrimaryScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, _userId.ToString()));
        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.SessionId,
            sessionId.ToString()));
        foreach (var method in authenticationMethods)
        {
            identity.AddClaim(new Claim(
                BrowserSessionClaimTypes.AuthenticationMethod,
                method));
        }

        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IssuedUtc = Now,
                ExpiresUtc = expiresAt
            },
            BrowserSessionAuthenticationDefaults.PrimaryScheme);
    }

    private string AssertSessionCookie(HttpContext context)
    {
        var cookies = context.Response.Headers.SetCookie
            .Where(value => value is not null)
            .Select(value => SetCookieHeaderValue.Parse(value!));
        var cookie = Assert.Single(
            cookies,
            value => value.Name.Equals(
                ApiAuthenticationDefaults.CookieName,
                StringComparison.Ordinal));
        Assert.True(cookie.Expires > _time.GetUtcNow());
        return $"{cookie.Name}={cookie.Value}";
    }

    private static DefaultHttpContext CreateHttpContext(
        IServiceProvider services,
        string? cookie = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.UserAgent = "account-session-persistence-test";
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = cookie;
        }

        services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
