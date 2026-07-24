using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class DatabaseReadinessTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task MigratedPostgresIsReady()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/health/ready",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnavailablePostgresFailsReadinessButNotLiveness()
    {
        await using var unavailable = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] =
                        "Host=127.0.0.1;Port=1;Database=missing;Username=missing;Timeout=1"
                })));
        using var client = unavailable.CreateClient(new()
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
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task ConnectedButUnmigratedPostgresFailsReadinessButNotLiveness()
    {
        var database = await factory.CreateUnmigratedDatabaseAsync(
            TestContext.Current.CancellationToken);
        try
        {
            await using var unmigrated = factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Postgres"] =
                                database.ConnectionString
                        })));
            using var client = unmigrated.CreateClient(new()
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
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally
        {
            await factory.DropDatabaseAsync(
                database.DatabaseName,
                CancellationToken.None);
        }
    }
}
