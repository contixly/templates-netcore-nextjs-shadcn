using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeySecurityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("GET", "/api/v1/account/api-keys", false)]
    [InlineData("POST", "/api/v1/account/api-keys", true)]
    [InlineData("PATCH", "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701", true)]
    [InlineData("DELETE", "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701", true)]
    [InlineData("POST", "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701/rotate", true)]
    [InlineData("GET", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys", false)]
    [InlineData("POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys", true)]
    public async Task EveryManagementRouteRequiresBrowserSession(string method, string path, bool unsafeOperation)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (unsafeOperation)
        {
            request.Headers.Add("X-CSRF-TOKEN", await LocalAuthTestClient.GetCsrfAsync(client));
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        await ApiKeyEndpointTests.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Theory]
    [InlineData("POST", "/api/v1/account/api-keys")]
    [InlineData("PATCH", "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701")]
    [InlineData("DELETE", "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701")]
    [InlineData("POST", "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701/rotate")]
    public async Task UnsafeManagementRoutesRequireCsrf(string method, string path)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "CSRF Key Owner",
            $"local-agent+csrf-{Guid.NewGuid():N}@local-agent.test");
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PATCH"
                ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                : null
        };
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        await ApiKeyEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "antiforgery_failed");
    }

    [Theory]
    [InlineData("{\"name\":\"Strict\",\"presetIds\":[\"basic-read\"],\"expiresIn\":\"30d\",\"rateLimitEnabled\":true,\"rateLimitMax\":1000,\"rateLimitWindow\":\"1h\",\"unknown\":true}")]
    [InlineData("{\"name\":\"Strict\",\"name\":\"Duplicate\",\"presetIds\":[\"basic-read\"],\"expiresIn\":\"30d\",\"rateLimitEnabled\":true,\"rateLimitMax\":1000,\"rateLimitWindow\":\"1h\"}")]
    [InlineData("{\"name\":42}")]
    [InlineData("not-json")]
    public async Task CreateRejectsUnknownDuplicateMalformedAndTypeInvalidJson(string body)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict Key Owner",
            $"local-agent+strict-{Guid.NewGuid():N}@local-agent.test");
        using var response = await ApiKeyEndpointTests.SendRawWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            body);
        await ApiKeyEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_request");
    }

    [Fact]
    public async Task BoundaryRejectsMissingFieldsInvalidUuidBodiesAndNoOpUpdate()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Boundary Key Owner",
            "local-agent+boundary-api-key@local-agent.test");

        using var missing = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client, HttpMethod.Post, "/api/v1/account/api-keys", new { name = "Missing" });
        await ApiKeyEndpointTests.AssertProblemAsync(missing, HttpStatusCode.BadRequest, "validation_failed");

        using var badUuid = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client, HttpMethod.Patch, "/api/v1/account/api-keys/not-a-uuid", new { name = "Updated" });
        await ApiKeyEndpointTests.AssertProblemAsync(badUuid, HttpStatusCode.BadRequest, "validation_failed");

        using var noOp = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client, HttpMethod.Patch,
            "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701", new { });
        await ApiKeyEndpointTests.AssertProblemAsync(noOp, HttpStatusCode.Conflict, "api_key_update_unchanged");

        using var rotateBody = await ApiKeyEndpointTests.SendRawWithCsrfAsync(
            client, HttpMethod.Post,
            "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701/rotate", "{}");
        await ApiKeyEndpointTests.AssertProblemAsync(rotateBody, HttpStatusCode.BadRequest, "invalid_request");

        using var deleteBody = await ApiKeyEndpointTests.SendRawWithCsrfAsync(
            client, HttpMethod.Delete,
            "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701", "{}");
        await ApiKeyEndpointTests.AssertProblemAsync(deleteBody, HttpStatusCode.BadRequest, "invalid_request");
    }

    [Fact]
    public async Task OrganizationMembersAreDeniedAndForeignOrMissingTargetsAreHidden()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient, "Tenant Owner", "local-agent+tenant-owner-key@local-agent.test");
        using var memberClient = factory.CreateApiClient();
        var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            memberClient, "Tenant Member", "local-agent+tenant-member-key@local-agent.test");
        using var organization = await OrganizationEndpointTestSupport.CreateOrganizationAsync(
            ownerClient, "Tenant Key Workspace");
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization)).GetProperty("id").GetGuid();
        using var addMember = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new { userId = member.UserId, role = "member" });
        Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);
        var memberId = (await ApiKeyEndpointTests.ReadDataAsync(addMember))
            .GetProperty("id")
            .GetGuid();

        using var memberList = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/api-keys",
            TestContext.Current.CancellationToken);
        await ApiKeyEndpointTests.AssertProblemAsync(
            memberList, HttpStatusCode.Forbidden, "api_key_permission_denied");
        using var memberMalformedCreate =
            await ApiKeyEndpointTests.SendRawWithCsrfAsync(
                memberClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/api-keys",
                "not-json");
        await ApiKeyEndpointTests.AssertProblemAsync(
            memberMalformedCreate,
            HttpStatusCode.Forbidden,
            "api_key_permission_denied");

        using var promote = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Patch,
            $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
            new { role = "admin" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        using var adminList = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/api-keys",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);

        using var created = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            ownerClient, HttpMethod.Post, "/api/v1/account/api-keys",
            ApiKeyEndpointTests.ValidCreate("Foreign personal key", "basic-read"));
        var keyId = (await ApiKeyEndpointTests.ReadDataAsync(created)).GetProperty("id").GetGuid();
        using var foreign = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            memberClient, HttpMethod.Patch, $"/api/v1/account/api-keys/{keyId:D}",
            new { name = "Must stay hidden" });
        await ApiKeyEndpointTests.AssertProblemAsync(
            foreign, HttpStatusCode.NotFound, "api_key_not_found");
    }

    [Fact]
    public async Task ManagementAuditNeverContainsCredentialHashOrRequestBody()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client, "Audit Key Owner", "local-agent+audit-api-key@local-agent.test");
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        const string sensitiveName = "round16-sensitive-key-name";
        using var create = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client, HttpMethod.Post, "/api/v1/account/api-keys",
            ApiKeyEndpointTests.ValidCreate(sensitiveName, "basic-read"));
        var data = await ApiKeyEndpointTests.ReadDataAsync(create);
        var credential = data.GetProperty("key").GetString()!;

        var audit = Assert.Single(logs.Logs, log =>
            log.Category == "Template.Api.Features.ApiKeys.ApiKeyEndpointModule" &&
            Equals(log.State.GetValueOrDefault("ApiKeyOperation"), "create"));
        Assert.Equal("succeeded", audit.State.GetValueOrDefault("ApiKeyOutcome"));
        var rendered = string.Join('\n', logs.Logs.Select(log =>
            $"{log.Message} {JsonSerializer.Serialize(log.State)} {JsonSerializer.Serialize(log.Scope)}"));
        Assert.DoesNotContain(credential, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyHash", rendered, StringComparison.OrdinalIgnoreCase);
    }
}
