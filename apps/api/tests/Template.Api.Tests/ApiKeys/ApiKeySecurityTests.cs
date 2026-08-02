using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Features.ApiKeys;
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
    [InlineData("PATCH", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701", true)]
    [InlineData("DELETE", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701", true)]
    [InlineData("POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701/rotate", true)]
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
    [InlineData("POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys")]
    [InlineData("PATCH", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701")]
    [InlineData("DELETE", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701")]
    [InlineData("POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57702/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701/rotate")]
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

    [Theory]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"presetIds\":null}")]
    [InlineData("{\"expiresIn\":null}")]
    [InlineData("{\"enabled\":null}")]
    [InlineData("{\"rateLimitEnabled\":null}")]
    [InlineData("{\"rateLimitMax\":null}")]
    [InlineData("{\"rateLimitWindow\":null}")]
    [InlineData("{\"enabled\":true,\"name\":null}")]
    public async Task PatchRejectsEveryExplicitNullRecognizedProperty(string body)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Null Patch Owner",
            $"local-agent+null-patch-{Guid.NewGuid():N}@local-agent.test");
        using var response = await ApiKeyEndpointTests.SendRawWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701",
            body);
        await ApiKeyEndpointTests.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
    }

    [Fact]
    public async Task PersonalOwnerCannotBeSelectedFromQueryAndEveryRouteRejectsUnexpectedQueryKeys()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Query Smuggling Owner",
            "local-agent+query-smuggling-key@local-agent.test");
        using var organization = await OrganizationEndpointTestSupport.CreateOrganizationAsync(
            client,
            "Query Smuggling Workspace");
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization))
            .GetProperty("id")
            .GetGuid();
        using var organizationKey = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/api-keys",
            ApiKeyEndpointTests.ValidCreate("Organization-only credential", "organization-read"));
        Assert.Equal(HttpStatusCode.Created, organizationKey.StatusCode);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        var cases = new[]
        {
            (HttpMethod.Get, $"/api/v1/account/api-keys?organizationId={organizationId:D}", (object?)null),
            (HttpMethod.Post, $"/api/v1/account/api-keys?organizationId={organizationId:D}", ApiKeyEndpointTests.ValidCreate("Smuggled", "basic-read")),
            (HttpMethod.Patch, "/api/v1/account/api-keys/0198a7ac-d0f8-7832-b711-211f56c57701?cursor=smuggled", (object)new { enabled = true }),
            (HttpMethod.Get, $"/api/v1/organizations/{organizationId:D}/api-keys?name=smuggled", (object?)null)
        };
        foreach (var (method, path, body) in cases)
        {
            using var response = body is null
                ? await client.SendAsync(
                    new HttpRequestMessage(method, path),
                    TestContext.Current.CancellationToken)
                : await ApiKeyEndpointTests.SendJsonWithCsrfAsync(client, method, path, body);
            await ApiKeyEndpointTests.AssertProblemAsync(
                response,
                HttpStatusCode.BadRequest,
                "validation_failed");
        }
        Assert.Equal(
            cases.Length,
            logs.Logs.Count(log =>
                log.Category == "Template.Api.Features.ApiKeys.ApiKeyEndpointModule" &&
                Equals(
                    log.State.GetValueOrDefault("ApiKeyOutcome"),
                    "validation_failed")));
    }

    [Fact]
    public async Task RevokedTargetsStayHiddenAndSameValuePatchIsAConflict()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Terminal Key Owner",
            "local-agent+terminal-key@local-agent.test");
        using var create = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            ApiKeyEndpointTests.ValidCreate("Stable terminal name", "basic-read"));
        var id = (await ApiKeyEndpointTests.ReadDataAsync(create)).GetProperty("id").GetGuid();

        using var sameValue = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/account/api-keys/{id:D}",
            new { name = "Stable terminal name" });
        await ApiKeyEndpointTests.AssertProblemAsync(
            sameValue,
            HttpStatusCode.Conflict,
            "api_key_update_unchanged");

        using var revoke = await ApiKeyEndpointTests.SendEmptyWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/account/api-keys/{id:D}");
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        foreach (var (method, path, body) in new[]
                 {
                     (HttpMethod.Delete, $"/api/v1/account/api-keys/{id:D}", (object?)null),
                     (HttpMethod.Post, $"/api/v1/account/api-keys/{id:D}/rotate", (object?)null),
                     (HttpMethod.Patch, $"/api/v1/account/api-keys/{id:D}", (object)new { enabled = false })
                 })
        {
            using var response = body is null
                ? await ApiKeyEndpointTests.SendEmptyWithCsrfAsync(client, method, path)
                : await ApiKeyEndpointTests.SendJsonWithCsrfAsync(client, method, path, body);
            await ApiKeyEndpointTests.AssertProblemAsync(
                response,
                HttpStatusCode.NotFound,
                "api_key_not_found");
        }
    }

    [Fact]
    public void SecretResponseStringFormattingRedactsCredentialButJsonRevealsIt()
    {
        const string credential = "sk_personal_round1-sensitive-credential";
        var now = DateTimeOffset.Parse("2026-08-02T12:00:00Z");
        var response = new ApiKeySecretResponse(
            Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701"),
            "user",
            Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702"),
            "Safe name",
            "sk_personal_round1",
            "active",
            true,
            ["basic:read"],
            true,
            1000,
            "1h",
            0,
            null,
            null,
            now.AddDays(30),
            null,
            now,
            now,
            credential);

        Assert.DoesNotContain(credential, response.ToString(), StringComparison.Ordinal);
        Assert.Contains(credential, JsonSerializer.Serialize(response), StringComparison.Ordinal);
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
        using var memberListWithUnknownQuery = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/api-keys?unknown=probe",
            TestContext.Current.CancellationToken);
        await ApiKeyEndpointTests.AssertProblemAsync(
            memberListWithUnknownQuery,
            HttpStatusCode.Forbidden,
            "api_key_permission_denied");
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

    [Fact]
    public async Task FailedManagementAuditContainsStableOutcomeButNoCredentialNameBodyOrHash()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Failure Audit Owner",
            "local-agent+failure-audit-key@local-agent.test");
        const string sensitiveName = "round1-failure-sensitive-name";
        using var create = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            ApiKeyEndpointTests.ValidCreate(sensitiveName, "basic-read"));
        var data = await ApiKeyEndpointTests.ReadDataAsync(create);
        var id = data.GetProperty("id").GetGuid();
        var credential = data.GetProperty("key").GetString()!;
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var unchanged = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/account/api-keys/{id:D}",
            new { name = sensitiveName });
        await ApiKeyEndpointTests.AssertProblemAsync(
            unchanged,
            HttpStatusCode.Conflict,
            "api_key_update_unchanged");

        var audit = Assert.Single(logs.Logs, log =>
            log.Category == "Template.Api.Features.ApiKeys.ApiKeyEndpointModule" &&
            Equals(log.State.GetValueOrDefault("ApiKeyOperation"), "update"));
        Assert.Equal(
            "api_key_update_unchanged",
            audit.State.GetValueOrDefault("ApiKeyOutcome"));
        var rendered = string.Join('\n', logs.Logs.Select(log =>
            $"{log.Message} {JsonSerializer.Serialize(log.State)} {JsonSerializer.Serialize(log.Scope)}"));
        Assert.DoesNotContain(credential, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyHash", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request body", rendered, StringComparison.OrdinalIgnoreCase);
    }
}
