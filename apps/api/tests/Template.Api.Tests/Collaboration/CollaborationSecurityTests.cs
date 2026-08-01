using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Tests.Collaboration;

public sealed class CollaborationSecurityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private const string CollaborationLogCategory =
        "Template.Api.Features.Collaboration.TeamEndpointModule";

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static TheoryData<string, string, bool> ProtectedRoutes => new()
    {
        { "GET", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams", false },
        { "POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams", true },
        { "PATCH", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702", true },
        { "DELETE", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702", true },
        { "GET", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members", false },
        { "POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members", true },
        { "DELETE", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members/0198a7ac-d0f8-7832-b711-211f56c57703", true },
        { "GET", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/member-candidates?q=test", false }
    };

    public static TheoryData<string, string, string> MutationBodies => new()
    {
        { "POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams", "{\"name\":\"CSRF Team\"}" },
        { "PATCH", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702", "{\"name\":\"CSRF Team\"}" },
        { "DELETE", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702", "" },
        { "POST", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members", "{\"userId\":\"0198a7ac-d0f8-7832-b711-211f56c57703\"}" },
        { "DELETE", "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members/0198a7ac-d0f8-7832-b711-211f56c57703", "" }
    };

    public static TheoryData<
        string,
        string,
        string?,
        bool,
        string,
        string> BoundaryCases => new()
        {
            {
                "GET",
                "/api/v1/organizations/sensitive-invalid-organization/teams",
                null,
                false,
                "team_list",
                "sensitive-invalid-organization"
            },
            {
                "POST",
                "/api/v1/organizations/sensitive-invalid-organization/teams",
                "{\"name\":\"Sensitive Team Name\"}",
                true,
                "team_create",
                "sensitive-invalid-organization"
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/sensitive-invalid-team",
                "{\"name\":\"Sensitive Team Name\"}",
                true,
                "team_update",
                "sensitive-invalid-team"
            },
            {
                "DELETE",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/sensitive-invalid-team",
                null,
                true,
                "team_delete",
                "sensitive-invalid-team"
            },
            {
                "GET",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/sensitive-invalid-team/members",
                null,
                false,
                "team_members_list",
                "sensitive-invalid-team"
            },
            {
                "POST",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/sensitive-invalid-team/members",
                "{\"userId\":\"0198a7ac-d0f8-7832-b711-211f56c57703\"}",
                true,
                "team_member_add",
                "sensitive-invalid-team"
            },
            {
                "DELETE",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members/sensitive-invalid-user",
                null,
                true,
                "team_member_remove",
                "sensitive-invalid-user"
            },
            {
                "GET",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/sensitive-invalid-team/member-candidates?q=SensitiveSearch",
                null,
                false,
                "team_candidates_list",
                "sensitive-invalid-team"
            }
        };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Every_team_route_requires_a_browser_session_and_is_no_store(
        string method,
        string path,
        bool hasBody)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (hasBody)
        {
            request.Content = JsonContent.Create(new { name = "not inspected" });
        }

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "unauthorized");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Theory]
    [MemberData(nameof(MutationBodies))]
    public async Task Every_team_mutation_requires_the_antiforgery_pair(
        string method,
        string path,
        string body)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "CSRF Team Owner",
            $"local-agent+team-csrf-{Guid.NewGuid():N}@local-agent.test");
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task Team_name_requests_reject_unknown_malformed_and_non_json_bodies(
        string method)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict Team Owner",
            $"local-agent+strict-team-{Guid.NewGuid():N}@local-agent.test");
        var path = method == "POST"
            ? $"/api/v1/organizations/{Guid.NewGuid():D}/teams"
            : $"/api/v1/organizations/{Guid.NewGuid():D}/teams/{Guid.NewGuid():D}";

        using var unknown = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            new HttpMethod(method),
            path,
            "{\"name\":\"Sensitive Strict Team\",\"unknown\":true}",
            "application/json");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            unknown,
            HttpStatusCode.BadRequest,
            "invalid_request");
        using var malformed = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            new HttpMethod(method),
            path,
            "{\"name\":",
            "application/json");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            malformed,
            HttpStatusCode.BadRequest,
            "invalid_request");
        using var nonJson = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            new HttpMethod(method),
            path,
            "{\"name\":\"Sensitive Strict Team\"}",
            "text/plain");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            nonJson,
            HttpStatusCode.BadRequest,
            "invalid_request");
    }

    [Fact]
    public async Task Add_member_rejects_unknown_json_fields()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict Team Member Owner",
            "local-agent+strict-team-member@local-agent.test");
        using var response = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/organizations/{Guid.NewGuid():D}/teams/{Guid.NewGuid():D}/members",
            $"{{\"userId\":\"{Guid.NewGuid():D}\",\"unknown\":true}}",
            "application/json");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
    }

    [Theory]
    [InlineData("/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702")]
    [InlineData("/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/teams/0198a7ac-d0f8-7832-b711-211f56c57702/members/0198a7ac-d0f8-7832-b711-211f56c57703")]
    public async Task Bodyless_team_deletes_reject_unexpected_request_bodies(
        string path)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Bodyless Delete Owner",
            $"local-agent+bodyless-delete-{Guid.NewGuid():N}@local-agent.test");

        using var response = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            HttpMethod.Delete,
            path,
            "{\"unexpected\":true}",
            "application/json");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Theory]
    [InlineData("not-a-uuid", "team_list", "organizationId")]
    [InlineData("{organizationId}/teams/not-a-uuid/members", "team_members_list", "teamId")]
    public async Task Route_UUIDs_are_canonical_and_rejected_after_actor_resolution(
        string pathSuffix,
        string expectedOperation,
        string expectedField)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Canonical Team Owner",
            "local-agent+canonical-team@local-agent.test");
        var organizationId = Guid.NewGuid();
        var path = pathSuffix.Replace(
            "{organizationId}",
            organizationId.ToString("D"),
            StringComparison.Ordinal);
        path = path == "not-a-uuid"
            ? $"/api/v1/organizations/{path}/teams"
            : $"/api/v1/organizations/{path}";
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            response,
            expectedField);
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category == CollaborationLogCategory);
        Assert.Equal(expectedOperation, audit.State["CollaborationOperation"]);
        Assert.Equal("validation_failed", audit.State["CollaborationOutcome"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("not-a-number")]
    [InlineData("999999999999999999999999999999")]
    public async Task Malformed_and_overflow_limits_fail_before_persistence(string limit)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Limit Team Owner",
            $"local-agent+team-limit-{Guid.NewGuid():N}@local-agent.test");

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{Guid.NewGuid():D}/teams?limit={limit}",
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            response,
            "limit");
        Assert.DoesNotContain(
            $"limit={limit}",
            RenderedLogs(factory.Services.GetRequiredService<CapturedLogProvider>()),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Candidate_query_over_one_hundred_fails_safely_before_persistence()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Query Team Owner",
            "local-agent+team-query@local-agent.test");
        var sensitiveQuery = new string('q', 101);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{Guid.NewGuid():D}/teams/{Guid.NewGuid():D}/member-candidates?q={sensitiveQuery}",
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            response,
            "q");
        Assert.DoesNotContain(
            sensitiveQuery,
            RenderedLogs(logs),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(BoundaryCases))]
    public async Task Every_team_route_rejects_at_the_boundary_with_one_safe_audit(
        string method,
        string path,
        string? body,
        bool requiresCsrf,
        string operation,
        string sensitiveToken)
    {
        var store = new BoundaryProbeTeamStore();
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITeamStore>();
                services.AddSingleton<ITeamStore>(store);
            }));
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Boundary Team Owner",
            $"local-agent+team-boundary-{Guid.NewGuid():N}@local-agent.test");
        var logs = isolated.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = requiresCsrf
            ? await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                client,
                new HttpMethod(method),
                path,
                body ?? string.Empty,
                "application/json")
            : await client.SendAsync(
                new HttpRequestMessage(new HttpMethod(method), path),
                TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            response,
            operation == "team_list" || operation == "team_create"
                ? "organizationId"
                : operation == "team_member_remove"
                    ? "userId"
                    : "teamId");
        OrganizationEndpointTestSupport.AssertNoStore(response);
        Assert.Equal(0, store.CallCount);
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category == CollaborationLogCategory);
        Assert.Equal(LogLevel.Information, audit.Level);
        Assert.Equal(operation, audit.State["CollaborationOperation"]);
        Assert.Equal("validation_failed", audit.State["CollaborationOutcome"]);
        Assert.IsType<Guid>(audit.State["UserId"]);
        Assert.IsType<Guid>(audit.State["SessionId"]);
        Assert.False(string.IsNullOrWhiteSpace(audit.Scope["TraceId"]?.ToString()));
        Assert.Null(audit.Exception);
        Assert.DoesNotContain(
            sensitiveToken,
            RenderedLogs(logs),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Sensitive Team Name",
            RenderedLogs(logs),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "SensitiveSearch",
            RenderedLogs(logs),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderedLogs(CapturedLogProvider logs) => string.Join(
        Environment.NewLine,
        logs.Logs.Select(log => string.Join(
            " ",
            new[] { log.Category, log.Message }
                .Concat(log.State.Values.Select(value => value?.ToString() ?? string.Empty))
                .Concat(log.Scope.Values.Select(value => value?.ToString() ?? string.Empty))
                .Append(log.Exception?.ToString() ?? string.Empty))));

    private sealed class BoundaryProbeTeamStore : ITeamStore
    {
        internal int CallCount { get; private set; }

        public Task<TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>> ListAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>>();

        public Task<TeamOperationResult<TeamSummary>> CreateAsync(
            CreateTeamCommand command,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamSummary>>();

        public Task<TeamOperationResult<TeamSummary>> UpdateAsync(
            UpdateTeamCommand command,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamSummary>>();

        public Task<TeamOperationResult<TeamDeletion>> DeleteAsync(
            DeleteTeamCommand command,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamDeletion>>();

        public Task<TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>> ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>>();

        public Task<TeamOperationResult<TeamMemberView>> AddMemberAsync(
            AddTeamMemberCommand command,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamMemberView>>();

        public Task<TeamOperationResult<TeamMemberRemoval>> RemoveMemberAsync(
            RemoveTeamMemberCommand command,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamMemberRemoval>>();

        public Task<TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>> ListCandidatesAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamId teamId,
            string? query,
            TeamCandidateCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) => Reached<TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>>();

        private Task<T> Reached<T>()
        {
            CallCount++;
            throw new InvalidOperationException(
                "The team store must not be reached by an HTTP boundary rejection.");
        }
    }
}
