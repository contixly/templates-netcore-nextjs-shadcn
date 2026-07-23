using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
        using var client = factory.CreateClient();

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
        using var client = unhealthyFactory.CreateClient();

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

    private sealed record HealthEnvelope(HealthData Data);
    private sealed record HealthData(string Status, DateTimeOffset Timestamp);
}
