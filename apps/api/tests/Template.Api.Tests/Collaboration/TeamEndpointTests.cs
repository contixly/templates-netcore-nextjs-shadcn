using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;

namespace Template.Api.Tests.Collaboration;

public sealed class TeamEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Team_lifecycle_uses_exact_routes_envelopes_locations_and_projections()
    {
        using var ownerClient = factory.CreateApiClient();
        var owner = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Team Owner",
            "local-agent+team-owner@local-agent.test");
        using var memberClient = factory.CreateApiClient();
        var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            memberClient,
            "Team Member",
            "local-agent+team-member@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Team Workspace");
        var organizationData =
            await OrganizationEndpointTestSupport.ReadDataAsync(organization);
        var organizationId = organizationData.GetProperty("id").GetGuid();
        Assert.True(organizationData.GetProperty("capabilities")
            .GetProperty("canManageTeams").GetBoolean());
        Assert.True(organizationData.GetProperty("capabilities")
            .GetProperty("canManageInvitations").GetBoolean());
        using var addOrganizationMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = member.UserId, role = "member" });
        Assert.Equal(HttpStatusCode.Created, addOrganizationMember.StatusCode);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var create =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams",
                new { name = "  Design  " });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await OrganizationEndpointTestSupport.ReadDataAsync(create);
        var teamId = created.GetProperty("id").GetGuid();
        Assert.Equal(
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}",
            create.Headers.Location?.OriginalString);
        AssertTeam(created, teamId, organizationId, "Design", 0);

        using var candidates = await ownerClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/member-candidates?q=Team%20Member&limit=50",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
        var candidatesData =
            await OrganizationEndpointTestSupport.ReadDataAsync(candidates);
        var candidate = Assert.Single(
            candidatesData.GetProperty("items").EnumerateArray());
        Assert.Equal(member.UserId, candidate.GetProperty("userId").GetGuid());
        Assert.Equal("Team Member", candidate.GetProperty("name").GetString());
        Assert.Equal(member.Email, candidate.GetProperty("email").GetString());
        Assert.Equal("member", candidate.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, candidate.GetProperty("imageUrl").ValueKind);
        Assert.NotEqual(
            default,
            candidate.GetProperty("joinedAt").GetDateTimeOffset());
        Assert.Equal(
            JsonValueKind.Null,
            candidatesData.GetProperty("nextCursor").ValueKind);

        using var addMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members",
                new { userId = member.UserId });
        Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);
        var added = await OrganizationEndpointTestSupport.ReadDataAsync(addMember);
        Assert.Equal(
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members/{member.UserId:D}",
            addMember.Headers.Location?.OriginalString);
        Assert.Equal(member.UserId, added.GetProperty("userId").GetGuid());
        Assert.NotEqual(default, added.GetProperty("id").GetGuid());
        Assert.Equal("Team Member", added.GetProperty("name").GetString());
        Assert.Equal(member.Email, added.GetProperty("email").GetString());
        Assert.Equal("member", added.GetProperty("role").GetString());
        Assert.NotEqual(
            default,
            added.GetProperty("organizationJoinedAt").GetDateTimeOffset());
        Assert.NotEqual(
            default,
            added.GetProperty("teamJoinedAt").GetDateTimeOffset());

        using var listMembers = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members?limit=50",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listMembers.StatusCode);
        var memberPage =
            await OrganizationEndpointTestSupport.ReadDataAsync(listMembers);
        Assert.Single(memberPage.GetProperty("items").EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            memberPage.GetProperty("nextCursor").ValueKind);

        using var list = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/teams?limit=50",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await OrganizationEndpointTestSupport.ReadDataAsync(list);
        var listed = Assert.Single(page.GetProperty("items").EnumerateArray());
        AssertTeam(listed, teamId, organizationId, "Design", 1);
        Assert.Single(
            listed.GetProperty("members").GetProperty("items").EnumerateArray());

        using var update =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}",
                new { name = "Platform" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        AssertTeam(
            await OrganizationEndpointTestSupport.ReadDataAsync(update),
            teamId,
            organizationId,
            "Platform",
            1);

        using var removeMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Delete,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members/{member.UserId:D}");
        Assert.Equal(HttpStatusCode.OK, removeMember.StatusCode);
        var removed =
            await OrganizationEndpointTestSupport.ReadDataAsync(removeMember);
        Assert.Equal(teamId, removed.GetProperty("teamId").GetGuid());
        Assert.Equal(member.UserId, removed.GetProperty("userId").GetGuid());

        using var delete =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Delete,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(
            teamId,
            (await OrganizationEndpointTestSupport.ReadDataAsync(delete))
                .GetProperty("teamId").GetGuid());

        OrganizationEndpointTestSupport.AssertNoStore(
            create,
            candidates,
            addMember,
            listMembers,
            list,
            update,
            removeMember,
            delete);
        var audits = logs.Logs
            .Where(log => log.Category ==
                "Template.Api.Features.Collaboration.TeamEndpointModule")
            .ToArray();
        Assert.Equal(8, audits.Length);
        Assert.Equal(
            new[]
            {
                "team_list",
                "team_create",
                "team_update",
                "team_delete",
                "team_members_list",
                "team_member_add",
                "team_member_remove",
                "team_candidates_list"
            }.Order(StringComparer.Ordinal),
            audits.Select(audit =>
                    Assert.IsType<string>(audit.State["CollaborationOperation"]))
                .Order(StringComparer.Ordinal));
        Assert.All(
            audits,
            audit => Assert.Equal("succeeded", audit.State["CollaborationOutcome"]));
        var renderedAudits = string.Join(
            Environment.NewLine,
            audits.Select(audit => string.Join(
                " ",
                new[] { audit.Message }
                    .Concat(audit.State.Values.Select(value =>
                        value?.ToString() ?? string.Empty)))));
        Assert.DoesNotContain("Team Owner", renderedAudits);
        Assert.DoesNotContain("Team Member", renderedAudits);
        Assert.DoesNotContain(owner.Email, renderedAudits);
        Assert.DoesNotContain(member.Email, renderedAudits);
        Assert.DoesNotContain("Design", renderedAudits);
        Assert.DoesNotContain("Platform", renderedAudits);
    }

    [Fact]
    public async Task Team_create_accepts_supplementary_plane_Unicode_scalars()
    {
        const string teamName = "\U00010400 \U0001D7CE";
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Unicode Team Owner",
            "local-agent+unicode-team-owner@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Unicode Team Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();

        using var create =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams",
                new { name = teamName });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(
            teamName,
            (await OrganizationEndpointTestSupport.ReadDataAsync(create))
            .GetProperty("name").GetString());
        OrganizationEndpointTestSupport.AssertNoStore(create);
    }

    [Fact]
    public async Task Member_can_read_but_cannot_mutate_teams()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Permission Owner",
            "local-agent+team-permission-owner@local-agent.test");
        using var memberClient = factory.CreateApiClient();
        var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            memberClient,
            "Permission Member",
            "local-agent+team-permission-member@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Permission Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        using var addOrganizationMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = member.UserId, role = "member" });
        using var create =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams",
                new { name = "Readable" });
        var teamId = (await OrganizationEndpointTestSupport.ReadDataAsync(create))
            .GetProperty("id").GetGuid();

        using var read = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/teams",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var candidateDenied = await memberClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/member-candidates?q=SensitiveCandidate",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            candidateDenied,
            HttpStatusCode.Forbidden,
            "team_permission_denied");
        Assert.DoesNotContain(
            "SensitiveCandidate",
            await candidateDenied.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);

        using var denied =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                memberClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}",
                new { name = "Sensitive Denied Name" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            denied,
            HttpStatusCode.Forbidden,
            "team_permission_denied");
        var body = await denied.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Sensitive Denied Name", body);
        OrganizationEndpointTestSupport.AssertNoStore(
            read,
            candidateDenied,
            denied);
    }

    [Fact]
    public async Task Team_failures_use_stable_status_and_problem_codes()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Failure Owner",
            "local-agent+team-failure-owner@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Failure Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        using var first = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/teams",
            new { name = "Duplicate" });
        var teamId = (await OrganizationEndpointTestSupport.ReadDataAsync(first))
            .GetProperty("id").GetGuid();
        using var duplicate =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams",
                new { name = "duplicate" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            duplicate,
            HttpStatusCode.Conflict,
            "team_name_conflict");
        using var unchanged =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}",
                new { name = "duplicate" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            unchanged,
            HttpStatusCode.Conflict,
            "team_name_unchanged");
        using var missing = await client.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/teams/{Guid.NewGuid():D}/members",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            missing,
            HttpStatusCode.NotFound,
            "team_not_found");
        using var absentMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members",
                new { userId = Guid.NewGuid() });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            absentMember,
            HttpStatusCode.NotFound,
            "team_member_not_found");
    }

    private static void AssertTeam(
        JsonElement value,
        Guid teamId,
        Guid organizationId,
        string name,
        int memberCount)
    {
        Assert.Equal(teamId, value.GetProperty("id").GetGuid());
        Assert.Equal(
            organizationId,
            value.GetProperty("organizationId").GetGuid());
        Assert.Equal(name, value.GetProperty("name").GetString());
        Assert.Equal(memberCount, value.GetProperty("memberCount").GetInt32());
        Assert.True(value.GetProperty("membersIncluded").GetBoolean());
        Assert.NotEqual(default, value.GetProperty("createdAt").GetDateTimeOffset());
        Assert.NotEqual(default, value.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.Equal(
            JsonValueKind.Array,
            value.GetProperty("members").GetProperty("items").ValueKind);
    }
}
