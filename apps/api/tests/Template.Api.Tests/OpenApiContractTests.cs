using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class OpenApiContractTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task TestHostPublishesVersionedOpenApi31Contract()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("3.1.", document["openapi"]!.GetValue<string>());
        Assert.NotNull(document["paths"]!["/api/health"]);
        Assert.NotNull(document["paths"]!["/api/health/live"]);
        Assert.NotNull(document["paths"]!["/api/health/ready"]);
        Assert.NotNull(document["paths"]!["/api/v1/system/status"]);
        Assert.NotNull(document["paths"]!["/api/v1/system/authenticated"]);
        Assert.Null(document["paths"]!["/api/testing/fault"]);
        Assert.Null(document["paths"]!["/api/testing/forbidden"]);
    }

    [Fact]
    public async Task CookieSchemeAppliesOnlyToProtectedOperation()
    {
        using var client = factory.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken))!;

        var scheme = document["components"]!["securitySchemes"]!["cookieAuth"]!;
        Assert.Equal("apiKey", scheme["type"]!.GetValue<string>());
        Assert.Equal("cookie", scheme["in"]!.GetValue<string>());
        Assert.Equal("__Host-template.session", scheme["name"]!.GetValue<string>());
        Assert.Null(document["paths"]!["/api/v1/system/status"]!["get"]!["security"]);
        Assert.NotNull(document["paths"]!["/api/v1/system/authenticated"]!["get"]!["security"]);
    }

    [Fact]
    public async Task ProductionHostDoesNotPublishRuntimeOpenApi()
    {
        await using var productionFactory = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
        using var client = productionFactory.CreateClient();

        using var response = await client.GetAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RuntimeDocumentSemanticallyMatchesCommittedContract()
    {
        using var client = factory.CreateClient();
        var runtime = JsonNode.Parse(await client.GetStringAsync(
            "/api/openapi/v1.json",
            TestContext.Current.CancellationToken));
        var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "contracts", "openapi", "v1.json"),
            TestContext.Current.CancellationToken));

        Assert.True(
            JsonNode.DeepEquals(committed, runtime),
            "Run the documented OpenAPI export command and commit contracts/openapi/v1.json.");
    }

    private static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Template.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Template.sln was not found above the test output directory.");
    }
}
