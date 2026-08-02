using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.ApiKeys;

public sealed class MachineOrganizationEndpointTests(
    ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PersonalKeyReadsOnlyCurrentMembershipsWithUserAccessProjection()
    {
        using var browser = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            browser,
            "Personal machine owner",
            $"local-agent+personal-machine-{Guid.NewGuid():N}@local-agent.test");
        using var firstCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                "Personal Machine One");
        using var secondCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                "Personal Machine Two");
        var firstId = (await ApiKeyEndpointTests.ReadDataAsync(firstCreate))
            .GetProperty("id")
            .GetGuid();
        var secondId = (await ApiKeyEndpointTests.ReadDataAsync(secondCreate))
            .GetProperty("id")
            .GetGuid();
        var key = await CreatePersonalKeyAsync(
            browser,
            "organization-read-all");

        using var foreignBrowser = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            foreignBrowser,
            "Foreign machine owner",
            $"local-agent+foreign-machine-{Guid.NewGuid():N}@local-agent.test");
        using var foreignCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                foreignBrowser,
                "Foreign Machine Organization");
        var foreignId = (await ApiKeyEndpointTests.ReadDataAsync(foreignCreate))
            .GetProperty("id")
            .GetGuid();
        await RemoveMembershipAsync(secondId);
        using var machine = factory.CreateApiClient();
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var list = await GetWithKeyAsync(
            machine,
            "/api/v1/organizations?limit=50",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await ApiKeyEndpointTests.ReadDataAsync(list);
        Assert.Equal(
            [firstId],
            page.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()));
        Assert.DoesNotContain(
            secondId,
            page.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("id").GetGuid()));
        Assert.All(page.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("user", item.GetProperty("accessPrincipal").GetString());
            Assert.Equal("owner", item.GetProperty("currentRole").GetString());
            Assert.True(item.GetProperty("capabilities")
                .GetProperty("canUpdateOrganization").GetBoolean());
        });
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);

        using var detail = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{firstId:D}",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailData = await ApiKeyEndpointTests.ReadDataAsync(detail);
        Assert.Equal(firstId, detailData.GetProperty("id").GetGuid());
        Assert.Equal("user", detailData.GetProperty("accessPrincipal").GetString());
        Assert.False(detailData.TryGetProperty("allowedEmailDomains", out _));

        using var members = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{firstId:D}/members?limit=50",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        Assert.Single((await ApiKeyEndpointTests.ReadDataAsync(members))
            .GetProperty("items").EnumerateArray());

        foreach (var path in new[]
                 {
                     $"/api/v1/organizations/{foreignId:D}",
                     $"/api/v1/organizations/{foreignId:D}/members"
                 })
        {
            using var foreign = await GetWithKeyAsync(
                machine,
                path,
                key.Credential);
            await ApiKeyEndpointTests.AssertProblemAsync(
                foreign,
                HttpStatusCode.Forbidden,
                "organization_access_denied");
            AssertNoCookie(foreign);
        }
        AssertNoCookie(list, detail, members);
        var machineAudits = logs.Logs
            .Where(log => log.State.ContainsKey("MachineApiOperation"))
            .ToArray();
        Assert.NotEmpty(machineAudits);
        Assert.Contains(
            machineAudits,
            log => Equals(
                log.State.GetValueOrDefault("MachineApiOutcome"),
                "organization_access_denied"));
        Assert.All(
            machineAudits,
            log => Assert.False(log.State.ContainsKey("SessionId")));
        Assert.DoesNotContain(
            key.Credential,
            string.Join('\n', machineAudits.Select(log => log.Message)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrganizationKeyReadsExactlyItsOwnerWithOrganizationAccessProjection()
    {
        using var browser = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            browser,
            "Organization machine creator",
            $"local-agent+organization-machine-{Guid.NewGuid():N}@local-agent.test");
        using var ownerCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                "Organization Machine Owner");
        using var foreignCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                "Organization Machine Foreign");
        var ownerId = (await ApiKeyEndpointTests.ReadDataAsync(ownerCreate))
            .GetProperty("id").GetGuid();
        var foreignId = (await ApiKeyEndpointTests.ReadDataAsync(foreignCreate))
            .GetProperty("id").GetGuid();
        var key = await CreateOrganizationKeyAsync(
            browser,
            ownerId,
            "organization-read-all");
        await RemoveMembershipAsync(ownerId);
        using var machine = factory.CreateApiClient();

        using var list = await GetWithKeyAsync(
            machine,
            "/api/v1/organizations",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var item = Assert.Single((await ApiKeyEndpointTests.ReadDataAsync(list))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(ownerId, item.GetProperty("id").GetGuid());
        Assert.Equal("organization", item.GetProperty("accessPrincipal").GetString());
        Assert.Equal("organization", item.GetProperty("currentRole").GetString());
        Assert.All(
            item.GetProperty("capabilities").EnumerateObject(),
            capability => Assert.False(capability.Value.GetBoolean()));

        using var detail = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{ownerId:D}",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailData = await ApiKeyEndpointTests.ReadDataAsync(detail);
        Assert.Equal("organization", detailData.GetProperty("accessPrincipal").GetString());
        Assert.False(detailData.TryGetProperty("allowedEmailDomains", out _));

        using var members = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{ownerId:D}/members",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);

        foreach (var path in new[]
                 {
                     $"/api/v1/organizations/{foreignId:D}",
                     $"/api/v1/organizations/{foreignId:D}/members"
                 })
        {
            using var denied = await GetWithKeyAsync(machine, path, key.Credential);
            await ApiKeyEndpointTests.AssertProblemAsync(
                denied,
                HttpStatusCode.Forbidden,
                "organization_access_denied");
        }
    }

    [Fact]
    public async Task MachineOrganizationReadsEnforceExactScopesAndConsumeQuotaOnce()
    {
        using var browser = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            browser,
            "Scoped machine owner",
            $"local-agent+scoped-machine-{Guid.NewGuid():N}@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                "Scoped Machine Organization");
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        var organizationRead = await CreatePersonalKeyAsync(
            browser,
            "organization-read");
        using var machine = factory.CreateApiClient();

        using var list = await GetWithKeyAsync(
            machine,
            "/api/v1/organizations",
            organizationRead.Credential);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var detail = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{organizationId:D}",
            organizationRead.Credential);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var members = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{organizationId:D}/members",
            organizationRead.Credential);
        await ApiKeyEndpointTests.AssertProblemAsync(
            members,
            HttpStatusCode.Forbidden,
            "api_key_permission_denied");
        var scopeAudit = Assert.Single(
            factory.Services.GetRequiredService<CapturedLogProvider>().Logs,
            log =>
                Equals(
                    log.State.GetValueOrDefault("MachineApiOperation"),
                    "organization_members_list") &&
                Equals(
                    log.State.GetValueOrDefault("MachineApiOutcome"),
                    "permission_denied"));
        Assert.Equal(
            organizationRead.Id,
            scopeAudit.State.GetValueOrDefault("ApiKeyId"));
        Assert.Equal(3, await RequestCountAsync(organizationRead.Id));

        var basic = await CreatePersonalKeyAsync(browser, "basic-read");
        using var denied = await GetWithKeyAsync(
            machine,
            "/api/v1/organizations",
            basic.Credential);
        await ApiKeyEndpointTests.AssertProblemAsync(
            denied,
            HttpStatusCode.Forbidden,
            "api_key_permission_denied");
        Assert.Equal(1, await RequestCountAsync(basic.Id));
    }

    [Fact]
    public async Task MachineUuidBoundaryBrowserDenialAndHeaderPrecedenceAreExact()
    {
        using var browser = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            browser,
            "Boundary machine owner",
            $"local-agent+boundary-machine-{Guid.NewGuid():N}@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                "Boundary Machine Organization");
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        var key = await CreatePersonalKeyAsync(browser, "organization-read");

        using var malformed = await GetWithKeyAsync(
            browser,
            "/api/v1/organizations/not-a-uuid",
            key.Credential);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            malformed,
            "organizationId");

        using var browserOnly = await browser.GetAsync(
            $"/api/v1/organizations/{organizationId:D}",
            TestContext.Current.CancellationToken);
        await ApiKeyEndpointTests.AssertProblemAsync(
            browserOnly,
            HttpStatusCode.Unauthorized,
            "api_key_missing");

        using var invalidHeader = await GetWithKeyAsync(
            browser,
            "/api/v1/organizations",
            "not-a-valid-api-key");
        await ApiKeyEndpointTests.AssertProblemAsync(
            invalidHeader,
            HttpStatusCode.Unauthorized,
            "api_key_invalid");
    }

    private static async Task<CreatedKey> CreatePersonalKeyAsync(
        HttpClient browser,
        string presetId)
    {
        using var response = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            browser,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            ApiKeyEndpointTests.ValidCreate(
                $"Personal machine {Guid.NewGuid():N}"[..32],
                presetId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await ApiKeyEndpointTests.ReadDataAsync(response);
        return new(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("key").GetString()!);
    }

    private static async Task<CreatedKey> CreateOrganizationKeyAsync(
        HttpClient browser,
        Guid organizationId,
        string presetId)
    {
        using var response = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            browser,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/api-keys",
            ApiKeyEndpointTests.ValidCreate(
                $"Organization machine {Guid.NewGuid():N}"[..32],
                presetId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await ApiKeyEndpointTests.ReadDataAsync(response);
        return new(
            data.GetProperty("id").GetGuid(),
            data.GetProperty("key").GetString()!);
    }

    private static async Task<HttpResponseMessage> GetWithKeyAsync(
        HttpClient client,
        string path,
        string credential)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Assert.True(request.Headers.TryAddWithoutValidation(
            "x-api-key",
            credential));
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private async Task<int> RequestCountAsync(Guid apiKeyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .ApiKeys.Where(key => key.Id == apiKeyId)
            .Select(key => key.RequestCount)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task RemoveMembershipAsync(Guid organizationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .OrganizationMembers
            .Where(member => member.OrganizationId == organizationId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertNoCookie(params HttpResponseMessage[] responses) =>
        Assert.All(responses, response =>
        {
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        });

    private sealed record CreatedKey(Guid Id, string Credential);
}
