using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class BrowserSessionSlidingExpirationTests(
    ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private readonly MutableTimeProvider _time = new(
        new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SsrReadDoesNotRenewBeforeBrowserRefreshReceivesCookie()
    {
        await using var timeControlled = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_time);
                services.PostConfigureAll<CookieAuthenticationOptions>(options =>
                    options.TimeProvider = _time);
            }));
        using var client = timeControlled.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        using var created = await LocalAuthTestClient.CreateScenarioAsync(client);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var original = await ReadOnlySessionAsync(timeControlled);

        _time.Advance(TimeSpan.FromDays(4));
        using var ssrRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/auth/session");
        ssrRequest.Headers.Add("X-Template-Session-Renewal", "suppress");
        using var ssrResponse = await client.SendAsync(
            ssrRequest,
            TestContext.Current.CancellationToken);
        var afterSsr = await ReadOnlySessionAsync(timeControlled);

        Assert.Equal(HttpStatusCode.OK, ssrResponse.StatusCode);
        Assert.False(HasSessionSetCookie(ssrResponse));
        Assert.Equal(original.UpdatedAt, afterSsr.UpdatedAt);
        Assert.Equal(original.ExpiresAt, afterSsr.ExpiresAt);

        using var browserResponse = await client.GetAsync(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var afterBrowser = await ReadOnlySessionAsync(timeControlled);

        Assert.Equal(HttpStatusCode.OK, browserResponse.StatusCode);
        Assert.True(HasSessionSetCookie(browserResponse));
        Assert.Equal(_time.GetUtcNow(), afterBrowser.UpdatedAt);
        Assert.Equal(
            _time.GetUtcNow() + ApiAuthenticationDefaults.Lifetime,
            afterBrowser.ExpiresAt);
    }

    private static async Task<AuthSessionEntity> ReadOnlySessionAsync(
        WebApplicationFactory<Program> application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static bool HasSessionSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) &&
        values.Any(value => value.StartsWith(
            $"{ApiAuthenticationDefaults.CookieName}=",
            StringComparison.Ordinal));

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan value) => _utcNow += value;
    }
}
