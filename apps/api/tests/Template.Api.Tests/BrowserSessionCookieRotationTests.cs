using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class BrowserSessionCookieRotationTests(PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private string _databaseName = string.Empty;
    private ServiceProvider _services = null!;
    private Guid _firstUserId;
    private Guid _secondUserId;
    private readonly SessionCommandBarrier _commandBarrier = new();
    private readonly List<bool> _primarySlidingRenewals = [];
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
        services.AddSingleton(_commandBarrier);
        services.AddDbContext<AuthDbContext>((provider, options) =>
        {
            AuthDbContext.Configure(options, database.ConnectionString);
            options.AddInterceptors(
                provider.GetRequiredService<SessionCommandBarrier>());
        });
        services.AddAuthInfrastructure(configuration);
        services.AddApiAuthentication();
        services.PostConfigure<CookieAuthenticationOptions>(
            ApiAuthenticationDefaults.SchemeName,
            options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.TimeProvider = _time;
                options.Events.OnCheckSlidingExpiration = context =>
                {
                    _primarySlidingRenewals.Add(context.ShouldRenew);
                    return Task.CompletedTask;
                };
            });
        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        _firstUserId = Guid.CreateVersion7();
        _secondUserId = Guid.CreateVersion7();
        db.Users.AddRange(
            CreateApplicationUser(
                _firstUserId,
                "local-agent+cookie-one@local-agent.test",
                "Cookie One"),
            CreateApplicationUser(
                _secondUserId,
                "local-agent+cookie-two@local-agent.test",
                "Cookie Two"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SameUserReloginRevokesOldCookieAndIssuesOneFreshCookie()
    {
        var oldCookie = await IssueCookieAsync(CreateAuthUser(
            _firstUserId,
            "local-agent+cookie-one@local-agent.test",
            "Cookie One"));

        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, oldCookie);
        var replacement = await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .SignInAsync(
                CreateAuthUser(
                    _firstUserId,
                    "local-agent+cookie-one@local-agent.test",
                    "Cookie One"),
                TestContext.Current.CancellationToken);
        var newCookie = AssertSingleLiveSessionCookie(context);

        Assert.NotEqual(oldCookie, newCookie);
        Assert.False((await AuthenticateAsync(oldCookie)).Succeeded);
        var newAuthentication = await AuthenticateAsync(newCookie);
        Assert.True(newAuthentication.Succeeded);
        Assert.Equal(
            _firstUserId.ToString(),
            newAuthentication.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        await AssertOnlySessionAsync(replacement.Id.Value);
    }

    [Fact]
    public async Task ReplacementSuppressesPrimaryHandlerSlidingRefresh()
    {
        var oldCookie = await IssueCookieAsync(CreateAuthUser(
            _firstUserId,
            "local-agent+cookie-one@local-agent.test",
            "Cookie One"));
        _time.Advance(TimeSpan.FromDays(4));

        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, oldCookie);
        var oldAuthentication = await context.AuthenticateAsync(
            ApiAuthenticationDefaults.SchemeName);
        Assert.True(oldAuthentication.Succeeded);
        Assert.Contains(true, _primarySlidingRenewals);

        var replacement = await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .SignInAsync(
                CreateAuthUser(
                    _secondUserId,
                    "local-agent+cookie-two@local-agent.test",
                    "Cookie Two"),
                TestContext.Current.CancellationToken);
        await context.Response.StartAsync(TestContext.Current.CancellationToken);

        var newCookie = AssertSingleLiveSessionCookie(context);
        Assert.NotEqual(oldCookie, newCookie);
        Assert.False((await AuthenticateAsync(oldCookie)).Succeeded);
        var newAuthentication = await AuthenticateAsync(newCookie);
        Assert.True(newAuthentication.Succeeded);
        Assert.Equal(
            _secondUserId.ToString(),
            newAuthentication.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        await AssertOnlySessionAsync(replacement.Id.Value);
    }

    [Fact]
    public async Task CrossUserSwitchRevokesOldCookieAndAuthenticatesOnlyNewUser()
    {
        var oldCookie = await IssueCookieAsync(CreateAuthUser(
            _firstUserId,
            "local-agent+cookie-one@local-agent.test",
            "Cookie One"));

        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, oldCookie);
        var replacement = await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .SignInAsync(
                CreateAuthUser(
                    _secondUserId,
                    "local-agent+cookie-two@local-agent.test",
                    "Cookie Two"),
                TestContext.Current.CancellationToken);
        var newCookie = AssertSingleLiveSessionCookie(context);

        Assert.False((await AuthenticateAsync(oldCookie)).Succeeded);
        var newAuthentication = await AuthenticateAsync(newCookie);
        Assert.True(newAuthentication.Succeeded);
        Assert.Equal(
            _secondUserId.ToString(),
            newAuthentication.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        await AssertOnlySessionAsync(replacement.Id.Value);
    }

    [Fact]
    public async Task OrdinaryLogoutRevokesSessionAndDeletesCookie()
    {
        var cookie = await IssueCookieAsync(CreateAuthUser(
            _firstUserId,
            "local-agent+cookie-one@local-agent.test",
            "Cookie One"));

        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, cookie);
        await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .SignOutAsync(TestContext.Current.CancellationToken);

        var deletion = Assert.Single(ParseSessionSetCookies(context));
        Assert.True(
            deletion.Expires <= _time.GetUtcNow() ||
            deletion.MaxAge <= TimeSpan.Zero);
        Assert.False((await AuthenticateAsync(cookie)).Succeeded);
        await AssertNoSessionsAsync();
    }

    [Fact]
    public async Task SecondGatewaySignInInOneRequestFailsClosed()
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        var gateway = scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>();
        var user = CreateAuthUser(
            _firstUserId,
            "local-agent+cookie-one@local-agent.test",
            "Cookie One");

        var session = await gateway.SignInAsync(
            user,
            TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.SignInAsync(user, TestContext.Current.CancellationToken));

        Assert.Contains("already", exception.Message, StringComparison.OrdinalIgnoreCase);
        AssertSingleLiveSessionCookie(context);
        await AssertOnlySessionAsync(session.Id.Value);
    }

    [Fact]
    public async Task SimultaneousReplacementAndRevocationNeverReuseOldCookieKey()
    {
        var oldCookie = await IssueCookieAsync(CreateAuthUser(
            _firstUserId,
            "local-agent+cookie-one@local-agent.test",
            "Cookie One"));
        _commandBarrier.CoordinateParallelSessionDeletes(2);

        await using var replacementScope = _services.CreateAsyncScope();
        await using var revocationScope = _services.CreateAsyncScope();
        HttpContext? replacementContext = null;

        async Task<BrowserSession> ReplaceAsync()
        {
            replacementContext = CreateHttpContext(
                replacementScope.ServiceProvider,
                oldCookie);
            return await replacementScope.ServiceProvider
                .GetRequiredService<IBrowserSessionGateway>()
                .SignInAsync(
                CreateAuthUser(
                    _secondUserId,
                    "local-agent+cookie-two@local-agent.test",
                    "Cookie Two"),
                TestContext.Current.CancellationToken);
        }

        async Task RevokeAsync()
        {
            CreateHttpContext(revocationScope.ServiceProvider, oldCookie);
            await revocationScope.ServiceProvider
                .GetRequiredService<IBrowserSessionGateway>()
                .SignOutAsync(TestContext.Current.CancellationToken);
        }

        var replacement = ReplaceAsync();
        var revocation = RevokeAsync();

        await _commandBarrier.ReleaseParallelCommandsAsync(
            Task.WhenAll(replacement, revocation),
            TestContext.Current.CancellationToken);
        var replacementSession = await replacement;
        var newCookie = AssertSingleLiveSessionCookie(replacementContext!);

        Assert.False((await AuthenticateAsync(oldCookie)).Succeeded);
        var newAuthentication = await AuthenticateAsync(newCookie);
        Assert.True(newAuthentication.Succeeded);
        Assert.Equal(
            _secondUserId.ToString(),
            newAuthentication.Principal!.FindFirstValue(ClaimTypes.NameIdentifier));
        await AssertOnlySessionAsync(replacementSession.Id.Value);
    }

    [Fact]
    public void PrimaryAndIssuerSchemesShareStoreFormatAndCookieSecurity()
    {
        using var scope = _services.CreateScope();
        var authentication = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<
                AuthenticationOptions>>()
            .Value;
        var monitor = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
                CookieAuthenticationOptions>>();
        var primary = monitor.Get(ApiAuthenticationDefaults.SchemeName);
        var issuer = monitor.Get(ApiAuthenticationDefaults.IssuerSchemeName);

        Assert.Same(_time, primary.TimeProvider);
        Assert.Same(primary.SessionStore, issuer.SessionStore);
        Assert.NotNull(primary.TicketDataFormat);
        Assert.NotNull(issuer.TicketDataFormat);
        Assert.Equal(ApiAuthenticationDefaults.CookieName, primary.Cookie.Name);
        Assert.Equal(primary.Cookie.Name, issuer.Cookie.Name);
        Assert.True(primary.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, primary.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, primary.Cookie.SameSite);
        Assert.Equal("/", primary.Cookie.Path);
        Assert.Null(primary.Cookie.Domain);
        Assert.Equal(
            ApiAuthenticationDefaults.DefaultSchemeName,
            authentication.DefaultAuthenticateScheme);
        Assert.Equal(
            ApiAuthenticationDefaults.SchemeName,
            authentication.DefaultChallengeScheme);
        Assert.Equal(
            ApiAuthenticationDefaults.SchemeName,
            authentication.DefaultForbidScheme);
        Assert.Equal(
            ApiAuthenticationDefaults.SchemeName,
            authentication.DefaultSignOutScheme);
        Assert.NotEqual(
            ApiAuthenticationDefaults.IssuerSchemeName,
            authentication.DefaultSignInScheme);
    }

    private async Task<string> IssueCookieAsync(AuthUser user)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider);
        await scope.ServiceProvider
            .GetRequiredService<IBrowserSessionGateway>()
            .SignInAsync(user, TestContext.Current.CancellationToken);
        return AssertSingleLiveSessionCookie(context);
    }

    private async Task<AuthenticateResult> AuthenticateAsync(string cookie)
    {
        await using var scope = _services.CreateAsyncScope();
        var context = CreateHttpContext(scope.ServiceProvider, cookie);
        return await context.AuthenticateAsync(ApiAuthenticationDefaults.SchemeName);
    }

    private async Task AssertOnlySessionAsync(Guid sessionId)
    {
        await using var scope = _services.CreateAsyncScope();
        var rows = await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions
            .AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(sessionId, Assert.Single(rows).Id);
    }

    private async Task AssertNoSessionsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions
            .AnyAsync(TestContext.Current.CancellationToken));
    }

    private string AssertSingleLiveSessionCookie(HttpContext context)
    {
        var cookie = Assert.Single(ParseSessionSetCookies(context));
        Assert.False(cookie.Value.Equals(string.Empty));
        Assert.True(cookie.Expires > _time.GetUtcNow());
        return $"{cookie.Name}={cookie.Value}";
    }

    private static IReadOnlyList<SetCookieHeaderValue> ParseSessionSetCookies(
        HttpContext context) =>
        context.Response.Headers.SetCookie
            .Where(value => value is not null)
            .Select(value => SetCookieHeaderValue.Parse(value!))
            .Where(cookie =>
                cookie.Name.Equals(
                    ApiAuthenticationDefaults.CookieName,
                    StringComparison.Ordinal))
            .ToArray();

    private static DefaultHttpContext CreateHttpContext(
        IServiceProvider services,
        string? cookie = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.UserAgent = "browser-session-cookie-test";
        if (cookie is not null)
        {
            context.Request.Headers.Cookie = cookie;
        }

        services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }

    private ApplicationUser CreateApplicationUser(
        Guid id,
        string email,
        string displayName) =>
        new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            IsLocalAutomation = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow()
        };

    private static AuthUser CreateAuthUser(
        Guid id,
        string email,
        string displayName) =>
        new(new UserId(id), displayName, email, false, null, true);

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
