using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class HealthEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/ready")]
    public async Task HealthyProbeReturnsEnvelopeAndDisablesCaching(string uri)
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(uri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var payload = await response.Content.ReadFromJsonAsync<HealthEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("healthy", payload!.Data.Status);
        Assert.NotEqual(default, payload.Data.Timestamp);
    }

    [Fact]
    public async Task FailedReadyCheckReturnsTyped503WhileLivenessRemainsHealthy()
    {
        await using var unhealthyFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck(
                    "test-ready",
                    () => HealthCheckResult.Unhealthy(),
                    tags: ["ready"])));
        using var client = unhealthyFactory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var ready = await client.GetAsync(
            "/api/health/ready",
            TestContext.Current.CancellationToken);
        using var live = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal("application/json", ready.Content.Headers.ContentType?.MediaType);
        var readyPayload = await ready.Content.ReadFromJsonAsync<HealthEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("unhealthy", readyPayload!.Data.Status);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Theory]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/live/")]
    public async Task ValidSessionCookieCannotReachTicketStoreOnLiveness(string uri)
    {
        var ticketStore = new FailingRetrieveTicketStore();
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<CookieAuthenticationOptions>(
                    ApiAuthenticationDefaults.SchemeName,
                    options =>
                    {
                        ticketStore.Inner = Assert.IsAssignableFrom<ITicketStore>(
                            options.SessionStore);
                        options.SessionStore = ticketStore;
                    })));
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        using var created = await LocalAuthTestClient.CreateScenarioAsync(client);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var authenticated = await client.GetAsync(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        var session = await authenticated.Content.ReadFromJsonAsync<SessionEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.True(session!.Data.Authenticated);
        Assert.True(ticketStore.TotalRetrieveAttempts > 0);
        var retrieveAttemptsBeforeFailure = ticketStore.TotalRetrieveAttempts;

        ticketStore.FailRetrieval = true;
        using var live = await client.GetAsync(
            uri,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var payload = await live.Content.ReadFromJsonAsync<HealthEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("healthy", payload!.Data.Status);
        Assert.Equal(0, ticketStore.RetrieveAttemptsAfterFailure);
        Assert.Equal(retrieveAttemptsBeforeFailure, ticketStore.TotalRetrieveAttempts);
    }

    private sealed record HealthEnvelope(HealthData Data);
    private sealed record HealthData(string Status, DateTimeOffset Timestamp);
    private sealed record SessionEnvelope(SessionData Data);
    private sealed record SessionData(bool Authenticated);

    private sealed class FailingRetrieveTicketStore : ITicketStore
    {
        internal ITicketStore Inner { private get; set; } = null!;
        internal bool FailRetrieval { private get; set; }
        internal int TotalRetrieveAttempts { get; private set; }
        internal int RetrieveAttemptsAfterFailure { get; private set; }

        public Task<string> StoreAsync(AuthenticationTicket ticket) =>
            Inner.StoreAsync(ticket);

        public Task<string> StoreAsync(
            AuthenticationTicket ticket,
            CancellationToken cancellationToken) =>
            Inner.StoreAsync(ticket, cancellationToken);

        public Task<string> StoreAsync(
            AuthenticationTicket ticket,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Inner.StoreAsync(ticket, httpContext, cancellationToken);

        public Task RenewAsync(string key, AuthenticationTicket ticket) =>
            Inner.RenewAsync(key, ticket);

        public Task RenewAsync(
            string key,
            AuthenticationTicket ticket,
            CancellationToken cancellationToken) =>
            Inner.RenewAsync(key, ticket, cancellationToken);

        public Task RenewAsync(
            string key,
            AuthenticationTicket ticket,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Inner.RenewAsync(key, ticket, httpContext, cancellationToken);

        public Task<AuthenticationTicket?> RetrieveAsync(string key)
        {
            ThrowIfUnavailable();
            return Inner.RetrieveAsync(key);
        }

        public Task<AuthenticationTicket?> RetrieveAsync(
            string key,
            CancellationToken cancellationToken)
        {
            ThrowIfUnavailable();
            return Inner.RetrieveAsync(key, cancellationToken);
        }

        public Task<AuthenticationTicket?> RetrieveAsync(
            string key,
            HttpContext httpContext,
            CancellationToken cancellationToken)
        {
            ThrowIfUnavailable();
            return Inner.RetrieveAsync(key, httpContext, cancellationToken);
        }

        public Task RemoveAsync(string key) =>
            Inner.RemoveAsync(key);

        public Task RemoveAsync(
            string key,
            CancellationToken cancellationToken) =>
            Inner.RemoveAsync(key, cancellationToken);

        public Task RemoveAsync(
            string key,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Inner.RemoveAsync(key, httpContext, cancellationToken);

        private void ThrowIfUnavailable()
        {
            TotalRetrieveAttempts++;
            if (!FailRetrieval)
            {
                return;
            }

            RetrieveAttemptsAfterFailure++;
            throw new IOException("Injected PostgreSQL ticket retrieval failure.");
        }
    }
}
