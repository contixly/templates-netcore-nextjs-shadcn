using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.ApiKeys;

public sealed class MachineTeamEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TeamReadRedactsEmbeddedMembersWhileTeamMemberReadIncludesThem()
    {
        using var browser = factory.CreateApiClient();
        var scenario = await CreateTeamScenarioAsync(browser, "Scoped teams");
        var teamOnly = await CreatePersonalKeyAsync(
            browser,
            "organization-teams-read");
        var organizationOnly = await CreatePersonalKeyAsync(
            browser,
            "organization-read");
        var teamMembers = await CreatePersonalKeyAsync(
            browser,
            "organization-team-members-read");
        using var machine = factory.CreateApiClient();

        using var redacted = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{scenario.OrganizationId:D}/teams?limit=50",
            teamOnly.Credential);
        Assert.Equal(HttpStatusCode.OK, redacted.StatusCode);
        var redactedTeam = Assert.Single(
            (await ApiKeyEndpointTests.ReadDataAsync(redacted))
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(scenario.TeamId, redactedTeam.GetProperty("id").GetGuid());
        Assert.Equal(1, redactedTeam.GetProperty("memberCount").GetInt32());
        Assert.False(redactedTeam.GetProperty("membersIncluded").GetBoolean());
        Assert.Empty(redactedTeam.GetProperty("members")
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            redactedTeam.GetProperty("members")
                .GetProperty("nextCursor")
                .ValueKind);

        using var included = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{scenario.OrganizationId:D}/teams",
            teamMembers.Credential);
        Assert.Equal(HttpStatusCode.OK, included.StatusCode);
        var includedTeam = Assert.Single(
            (await ApiKeyEndpointTests.ReadDataAsync(included))
            .GetProperty("items")
            .EnumerateArray());
        Assert.True(includedTeam.GetProperty("membersIncluded").GetBoolean());
        var embedded = Assert.Single(includedTeam.GetProperty("members")
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(scenario.MemberUserId, embedded.GetProperty("userId").GetGuid());

        using var deniedMembers = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{scenario.OrganizationId:D}/teams/{scenario.TeamId:D}/members",
            teamOnly.Credential);
        await ApiKeyEndpointTests.AssertProblemAsync(
            deniedMembers,
            HttpStatusCode.Forbidden,
            "api_key_permission_denied");

        foreach (var path in new[]
                 {
                     $"/api/v1/organizations/{scenario.OrganizationId:D}/teams",
                     $"/api/v1/organizations/{scenario.OrganizationId:D}/teams/{scenario.TeamId:D}/members"
                 })
        {
            using var denied = await GetWithKeyAsync(
                machine,
                path,
                organizationOnly.Credential);
            await ApiKeyEndpointTests.AssertProblemAsync(
                denied,
                HttpStatusCode.Forbidden,
                "api_key_permission_denied");
            AssertNoCookie(denied);
        }

        using var members = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{scenario.OrganizationId:D}/teams/{scenario.TeamId:D}/members?limit=50",
            teamMembers.Credential);
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        var listedMember = Assert.Single(
            (await ApiKeyEndpointTests.ReadDataAsync(members))
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(
            scenario.MemberUserId,
            listedMember.GetProperty("userId").GetGuid());

        Assert.Equal(2, await RequestCountAsync(teamOnly.Id));
        Assert.Equal(2, await RequestCountAsync(organizationOnly.Id));
        Assert.Equal(2, await RequestCountAsync(teamMembers.Id));
        AssertNoCookie(redacted, included, deniedMembers, members);

        var audits = factory.Services.GetRequiredService<CapturedLogProvider>()
            .Logs
            .Where(log => log.State.ContainsKey("MachineApiOperation"))
            .ToArray();
        Assert.Contains(audits, log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "team_list") &&
            Equals(log.State.GetValueOrDefault("MachineApiOutcome"), "succeeded"));
        Assert.Contains(audits, log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "team_members_list") &&
            Equals(log.State.GetValueOrDefault("MachineApiOutcome"), "permission_denied"));
        var rendered = string.Join(
            '\n',
            audits.Select(log => string.Join(
                ' ',
                new[] { log.Message }.Concat(log.State.Values.Select(value =>
                    value?.ToString() ?? string.Empty)))));
        Assert.DoesNotContain(teamOnly.Credential, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(teamMembers.Credential, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.MemberEmail, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Machine team", rendered, StringComparison.Ordinal);
        Assert.All(audits, audit => Assert.False(audit.State.ContainsKey("SessionId")));
    }

    [Fact]
    public async Task PersonalKeyTeamAccessTracksCurrentOrganizationMembership()
    {
        using var browser = factory.CreateApiClient();
        var scenario = await CreateTeamScenarioAsync(browser, "Current membership");
        var key = await CreatePersonalKeyAsync(
            browser,
            "organization-team-members-read");
        using var machine = factory.CreateApiClient();

        using var beforeRemoval = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{scenario.OrganizationId:D}/teams",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, beforeRemoval.StatusCode);

        await RemoveMembershipAsync(scenario.OrganizationId);

        foreach (var path in new[]
                 {
                     $"/api/v1/organizations/{scenario.OrganizationId:D}/teams",
                     $"/api/v1/organizations/{scenario.OrganizationId:D}/teams/{scenario.TeamId:D}/members"
                 })
        {
            using var denied = await GetWithKeyAsync(machine, path, key.Credential);
            await ApiKeyEndpointTests.AssertProblemAsync(
                denied,
                HttpStatusCode.Forbidden,
                "organization_access_denied");
            AssertNoCookie(denied);
        }

        Assert.Equal(3, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task OrganizationKeyIsTenantBoundAndForeignTeamIdsAreNotDisclosed()
    {
        using var browser = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            browser,
            "Organization team creator",
            $"local-agent+organization-team-{Guid.NewGuid():N}@local-agent.test");
        var owner = await CreateOrganizationWithTeamAsync(
            browser,
            "Organization key owner",
            "Owner team");
        var foreign = await CreateOrganizationWithTeamAsync(
            browser,
            "Organization key foreign",
            "Foreign team");
        var key = await CreateOrganizationKeyAsync(
            browser,
            owner.OrganizationId,
            "organization-team-members-read");
        await RemoveMembershipAsync(owner.OrganizationId);
        using var machine = factory.CreateApiClient();

        using var ownTeams = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{owner.OrganizationId:D}/teams",
            key.Credential);
        Assert.Equal(HttpStatusCode.OK, ownTeams.StatusCode);
        Assert.Equal(
            owner.TeamId,
            Assert.Single((await ApiKeyEndpointTests.ReadDataAsync(ownTeams))
                .GetProperty("items")
                .EnumerateArray())
                .GetProperty("id")
                .GetGuid());

        using var crossOrganization = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{foreign.OrganizationId:D}/teams",
            key.Credential);
        await ApiKeyEndpointTests.AssertProblemAsync(
            crossOrganization,
            HttpStatusCode.Forbidden,
            "organization_access_denied");

        using var foreignTeamId = await GetWithKeyAsync(
            machine,
            $"/api/v1/organizations/{owner.OrganizationId:D}/teams/{foreign.TeamId:D}/members",
            key.Credential);
        await ApiKeyEndpointTests.AssertProblemAsync(
            foreignTeamId,
            HttpStatusCode.NotFound,
            "team_not_found");

        Assert.Equal(3, await RequestCountAsync(key.Id));
        AssertNoCookie(ownTeams, crossOrganization, foreignTeamId);
    }

    [Fact]
    public async Task MixedTeamReadsUseStrictApiKeyHeaderPrecedence()
    {
        using var browser = factory.CreateApiClient();
        var scenario = await CreateTeamScenarioAsync(browser, "Header precedence");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/organizations/{scenario.OrganizationId:D}/teams");
        Assert.True(request.Headers.TryAddWithoutValidation(
            "x-api-key",
            "not-a-valid-api-key"));
        using var response = await browser.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await ApiKeyEndpointTests.AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "api_key_invalid");
        AssertNoCookie(response);
    }

    private async Task<TeamScenario> CreateTeamScenarioAsync(
        HttpClient browser,
        string organizationName)
    {
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            browser,
            $"{organizationName} owner",
            $"local-agent+{Guid.NewGuid():N}@local-agent.test");
        using var memberBrowser = factory.CreateApiClient();
        var memberEmail =
            $"local-agent+team-member-{Guid.NewGuid():N}@local-agent.test";
        var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            memberBrowser,
            $"{organizationName} member",
            memberEmail);

        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                organizationName);
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization))
            .GetProperty("id")
            .GetGuid();
        using var addOrganizationMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                browser,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = member.UserId, role = "member" });
        Assert.Equal(HttpStatusCode.Created, addOrganizationMember.StatusCode);
        using var team = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            browser,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/teams",
            new { name = "Machine team" });
        Assert.Equal(HttpStatusCode.Created, team.StatusCode);
        var teamId = (await ApiKeyEndpointTests.ReadDataAsync(team))
            .GetProperty("id")
            .GetGuid();
        using var addTeamMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                browser,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members",
                new { userId = member.UserId });
        Assert.Equal(HttpStatusCode.Created, addTeamMember.StatusCode);

        return new(organizationId, teamId, member.UserId, memberEmail);
    }

    private static async Task<OrganizationTeam> CreateOrganizationWithTeamAsync(
        HttpClient browser,
        string organizationName,
        string teamName)
    {
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                browser,
                organizationName);
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization))
            .GetProperty("id")
            .GetGuid();
        using var team = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            browser,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/teams",
            new { name = teamName });
        Assert.Equal(HttpStatusCode.Created, team.StatusCode);
        var teamId = (await ApiKeyEndpointTests.ReadDataAsync(team))
            .GetProperty("id")
            .GetGuid();
        return new(organizationId, teamId);
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
                $"Machine team {Guid.NewGuid():N}"[..32],
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
                $"Machine team {Guid.NewGuid():N}"[..32],
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
            .ApiKeys
            .Where(key => key.Id == apiKeyId)
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
    private sealed record OrganizationTeam(Guid OrganizationId, Guid TeamId);
    private sealed record TeamScenario(
        Guid OrganizationId,
        Guid TeamId,
        Guid MemberUserId,
        string MemberEmail);
}
