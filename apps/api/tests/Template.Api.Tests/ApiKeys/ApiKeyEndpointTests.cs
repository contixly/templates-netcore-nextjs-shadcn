using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeyEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PersonalApiKeyLifecycleUsesExactRoutesAndRevealOnceResponses()
    {
        using var client = factory.CreateApiClient();
        var actor = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Personal Key Owner",
            "local-agent+personal-api-key@local-agent.test");

        using var create = await SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            ValidCreate(" Personal automation ", "basic-read"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadDataAsync(create);
        var id = created.GetProperty("id").GetGuid();
        var credential = created.GetProperty("key").GetString();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("Personal automation", created.GetProperty("name").GetString());
        Assert.Equal("user", created.GetProperty("ownerKind").GetString());
        Assert.Equal(actor.UserId, created.GetProperty("ownerId").GetGuid());
        Assert.NotEqual(
            JsonValueKind.Null,
            created.GetProperty("expiresAt").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(credential));
        Assert.Equal(
            $"/api/v1/account/api-keys/{id:D}",
            create.Headers.Location?.OriginalString);

        using var list = await client.GetAsync(
            "/api/v1/account/api-keys",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await ReadDataAsync(list);
        var listed = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(id, listed.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        Assert.DoesNotContain("\"key\"", listed.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("hash", listed.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var update = await SendJsonWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/account/api-keys/{id:D}",
            new { name = "Renamed personal key", enabled = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadDataAsync(update);
        Assert.Equal("Renamed personal key", updated.GetProperty("name").GetString());
        Assert.False(updated.GetProperty("enabled").GetBoolean());
        Assert.DoesNotContain("\"key\"", updated.GetRawText(), StringComparison.Ordinal);

        using var rotate = await SendEmptyWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/account/api-keys/{id:D}/rotate");
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var rotated = await ReadDataAsync(rotate);
        Assert.Equal(id, rotated.GetProperty("id").GetGuid());
        Assert.NotEqual(credential, rotated.GetProperty("key").GetString());

        using var revoke = await SendEmptyWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/account/api-keys/{id:D}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revoked = await ReadDataAsync(revoke);
        Assert.Equal(id, revoked.GetProperty("id").GetGuid());
        Assert.NotEqual(default, revoked.GetProperty("revokedAt").GetDateTimeOffset());
        Assert.DoesNotContain(
            credential!,
            await revoke.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));

        using var afterRevoke = await client.GetAsync(
            "/api/v1/account/api-keys?limit=50",
            TestContext.Current.CancellationToken);
        Assert.Empty((await ReadDataAsync(afterRevoke)).GetProperty("items").EnumerateArray());
        AssertNoStore(create, list, update, rotate, revoke, afterRevoke);
    }

    [Fact]
    public async Task OrganizationApiKeyLifecycleUsesOwnerQualifiedRoutes()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Organization Key Owner",
            "local-agent+organization-api-key@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "API Key Workspace");
        var organizationId = (await ReadDataAsync(organization))
            .GetProperty("id")
            .GetGuid();
        var root = $"/api/v1/organizations/{organizationId:D}/api-keys";

        using var create = await SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            root,
            ValidCreate("Workspace automation", "organization-read-all"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadDataAsync(create);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("organization", created.GetProperty("ownerKind").GetString());
        Assert.Equal(organizationId, created.GetProperty("ownerId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("key").GetString()));

        using var list = await client.GetAsync(root, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(id, Assert.Single((await ReadDataAsync(list))
            .GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        using var update = await SendJsonWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"{root}/{id:D}",
            new { rateLimitEnabled = false, rateLimitMax = 2000, rateLimitWindow = "1d" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False((await ReadDataAsync(update)).GetProperty("rateLimitEnabled").GetBoolean());

        using var rotate = await SendEmptyWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"{root}/{id:D}/rotate");
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        Assert.True((await ReadDataAsync(rotate)).TryGetProperty("key", out _));

        using var revoke = await SendEmptyWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"{root}/{id:D}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        AssertNoStore(create, list, update, rotate, revoke);
    }

    [Fact]
    public async Task ManagementPaginationIsOpaqueBoundedAndDefaultsToFifty()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Pagination Key Owner",
            "local-agent+pagination-api-key@local-agent.test");
        for (var index = 0; index < 2; index++)
        {
            using var created = await SendJsonWithCsrfAsync(
                client,
                HttpMethod.Post,
                "/api/v1/account/api-keys",
                ValidCreate($"Pagination {index}", "basic-read"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var first = await client.GetAsync(
            "/api/v1/account/api-keys?limit=1",
            TestContext.Current.CancellationToken);
        var firstPage = await ReadDataAsync(first);
        Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        using var second = await client.GetAsync(
            $"/api/v1/account/api-keys?limit=1&cursor={Uri.EscapeDataString(cursor!)}",
            TestContext.Current.CancellationToken);
        Assert.Single((await ReadDataAsync(second)).GetProperty("items").EnumerateArray());

        foreach (var path in new[]
                 {
                     "/api/v1/account/api-keys?limit=0",
                     "/api/v1/account/api-keys?limit=101",
                     "/api/v1/account/api-keys?limit=1&cursor=not-opaque"
                 })
        {
            using var invalid = await client.GetAsync(path, TestContext.Current.CancellationToken);
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "validation_failed");
        }
    }

    internal static object ValidCreate(string name, string presetId) => new
    {
        name,
        presetIds = new[] { presetId },
        expiresIn = "30d",
        rateLimitEnabled = true,
        rateLimitMax = 1000,
        rateLimitWindow = "1h"
    };

    internal static async Task<HttpResponseMessage> SendJsonWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SendRawWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string body,
        string mediaType = "application/json")
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SendEmptyWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", await LocalAuthTestClient.GetCsrfAsync(client));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    internal static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    internal static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = document.RootElement;
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal($"urn:template:problem:{code}", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    internal static void AssertNoStore(params HttpResponseMessage[] responses) =>
        Assert.All(responses, response =>
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString()));
}
