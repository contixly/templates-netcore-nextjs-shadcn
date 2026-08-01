using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
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

    public static TheoryData<TeamMutationKind> TeamMutationKinds => new()
    {
        TeamMutationKind.Create,
        TeamMutationKind.Update,
        TeamMutationKind.Delete,
        TeamMutationKind.AddMember,
        TeamMutationKind.RemoveMember
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
        using var wrongRequest = new HttpRequestMessage(
            new HttpMethod(method),
            path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        wrongRequest.Headers.Add("X-CSRF-TOKEN", "wrong-request-token");
        using var wrong = await client.SendAsync(
            wrongRequest,
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            wrong,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
        OrganizationEndpointTestSupport.AssertNoStore(response, wrong);
        Assert.DoesNotContain(
            factory.Services.GetRequiredService<CapturedLogProvider>().Logs,
            log => log.Category == CollaborationLogCategory);
    }

    [Theory]
    [MemberData(nameof(TeamMutationKinds))]
    public async Task Every_team_mutation_rejects_unsafe_bodies_before_storage_without_leaking(
        TeamMutationKind kind)
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
        var actor = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict Mutation Owner",
            $"local-agent+strict-mutation-{kind}-{Guid.NewGuid():N}@local-agent.test");
        var organizationId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var sentinel = $"SENSITIVE-TEAM-BODY-{kind}-{Guid.NewGuid():N}";
        var teamPath =
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}";
        var (method, path, body, operation) = kind switch
        {
            TeamMutationKind.Create => (
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams",
                $"{{\"name\":\"Boundary Team\",\"unknown\":\"{sentinel}\"}}",
                "team_create"),
            TeamMutationKind.Update => (
                HttpMethod.Patch,
                teamPath,
                $"{{\"name\":\"Boundary Team\",\"unknown\":\"{sentinel}\"}}",
                "team_update"),
            TeamMutationKind.Delete => (
                HttpMethod.Delete,
                teamPath,
                $"{{\"unexpected\":\"{sentinel}\"}}",
                "team_delete"),
            TeamMutationKind.AddMember => (
                HttpMethod.Post,
                $"{teamPath}/members",
                $"{{\"userId\":\"{targetUserId:D}\",\"unknown\":\"{sentinel}\"}}",
                "team_member_add"),
            TeamMutationKind.RemoveMember => (
                HttpMethod.Delete,
                $"{teamPath}/members/{targetUserId:D}",
                $"{{\"unexpected\":\"{sentinel}\"}}",
                "team_member_remove"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var logs = isolated.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            method,
            path,
            body,
            "application/json");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
        OrganizationEndpointTestSupport.AssertNoStore(response);
        Assert.Equal(0, store.CallCount);
        var audit = AssertSingleFinalAudit(logs, operation, "invalid_request");
        Assert.Equal(actor.UserId, audit.State["UserId"]);
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "CollaborationOperation",
                "CollaborationOutcome",
                "UserId",
                "SessionId",
                "OrganizationId",
                "TeamId",
                "TargetUserId",
                "ResultCount",
                "{OriginalFormat}"
            },
            audit.State.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "TraceId" },
            audit.Scope.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.DoesNotContain(
            sentinel,
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            sentinel,
            RenderedLogs(logs),
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TeamMutationKinds))]
    public async Task Every_team_mutation_enforces_complete_role_and_resource_matrix(
        TeamMutationKind kind)
    {
        using var scenario = await CreateMutationScenarioAsync(kind);
        var (operation, deniedBodyToken) = kind switch
        {
            TeamMutationKind.Create => ("team_create", "Sensitive Member Create"),
            TeamMutationKind.Update => ("team_update", "Sensitive Member Update"),
            TeamMutationKind.Delete => ("team_delete", (string?)null),
            TeamMutationKind.AddMember => ("team_member_add", null),
            TeamMutationKind.RemoveMember => ("team_member_remove", null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        scenario.Logs.Clear();
        using var denied = await SendMutationAsync(
            scenario,
            kind,
            MutationAttempt.MemberDenied);
        await AssertMutationProblemAsync(
            denied,
            HttpStatusCode.Forbidden,
            "team_permission_denied",
            scenario.Logs,
            operation,
            deniedBodyToken);

        scenario.Logs.Clear();
        using var succeeded = await SendMutationAsync(
            scenario,
            kind,
            MutationAttempt.AdminSuccess);
        Assert.Equal(
            kind is TeamMutationKind.Create or TeamMutationKind.AddMember
                ? HttpStatusCode.Created
                : HttpStatusCode.OK,
            succeeded.StatusCode);
        OrganizationEndpointTestSupport.AssertNoStore(succeeded);
        AssertSingleFinalAudit(scenario.Logs, operation, "succeeded");

        scenario.Logs.Clear();
        using var missing = await SendMutationAsync(
            scenario,
            kind,
            MutationAttempt.MissingResource);
        await AssertMutationProblemAsync(
            missing,
            HttpStatusCode.NotFound,
            "team_not_found",
            scenario.Logs,
            operation);

        scenario.Logs.Clear();
        using var foreign = await SendMutationAsync(
            scenario,
            kind,
            MutationAttempt.ForeignResource);
        await AssertMutationProblemAsync(
            foreign,
            HttpStatusCode.NotFound,
            "team_not_found",
            scenario.Logs,
            operation);

        if (kind is TeamMutationKind.AddMember or TeamMutationKind.RemoveMember)
        {
            scenario.Logs.Clear();
            using var missingMember = await SendMutationAsync(
                scenario,
                kind,
                MutationAttempt.MissingMember);
            await AssertMutationProblemAsync(
                missingMember,
                HttpStatusCode.NotFound,
                "team_member_not_found",
                scenario.Logs,
                operation);
        }
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

    [Fact]
    public async Task Unexpected_team_store_failure_emits_one_safe_final_outcome_audit()
    {
        var store = new FaultProbeTeamStore(FaultProbeMode.ThrowFromList);
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
            "Unexpected Audit Actor",
            "local-agent+unexpected-team-audit@local-agent.test");
        var logs = isolated.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{Guid.NewGuid():D}/teams",
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal_error");
        OrganizationEndpointTestSupport.AssertNoStore(response);
        Assert.Equal(1, store.CallCount);
        AssertSingleFinalAudit(logs, "team_list", "unexpected_failure");
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category == CollaborationLogCategory);
        Assert.DoesNotContain("Sensitive store exception", audit.Message);
        Assert.DoesNotContain(
            "Sensitive store exception",
            string.Join(" ", audit.State.Values));
        Assert.Null(audit.Exception);
    }

    [Fact]
    public async Task Projection_failure_is_not_audited_as_success()
    {
        var store = new FaultProbeTeamStore(
            FaultProbeMode.ThrowDuringCandidateProjection);
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
            "Projection Audit Actor",
            "local-agent+projection-team-audit@local-agent.test");
        var logs = isolated.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await client.GetAsync(
            $"/api/v1/organizations/{Guid.NewGuid():D}/teams/{Guid.NewGuid():D}/member-candidates",
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal_error");
        OrganizationEndpointTestSupport.AssertNoStore(response);
        Assert.Equal(1, store.CallCount);
        AssertSingleFinalAudit(
            logs,
            "team_candidates_list",
            "unexpected_failure");
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category == CollaborationLogCategory);
        Assert.DoesNotContain("Sensitive projection exception", audit.Message);
        Assert.DoesNotContain(
            "Sensitive projection exception",
            string.Join(" ", audit.State.Values));
        Assert.DoesNotContain(
            logs.Logs.Where(log => log.Category == CollaborationLogCategory),
            audit => Equals("succeeded", audit.State["CollaborationOutcome"]));
    }

    [Fact]
    public async Task Delete_rejects_body_detection_feature_without_length_or_transfer_encoding()
    {
        var organizationId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var path = $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}";
        var store = new BoundaryProbeTeamStore();
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITeamStore>();
                services.AddSingleton<ITeamStore>(store);
                services.AddSingleton<IStartupFilter>(
                    new RequestBodyDetectionStartupFilter(path));
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
            "Body Feature Audit Actor",
            "local-agent+body-feature-team-audit@local-agent.test");
        var logs = isolated.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            HttpMethod.Delete,
            path,
            "body-without-framing-headers",
            "application/octet-stream");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
        OrganizationEndpointTestSupport.AssertNoStore(response);
        Assert.Equal(0, store.CallCount);
        AssertSingleFinalAudit(logs, "team_delete", "invalid_request");
    }

    private static string RenderedLogs(CapturedLogProvider logs) => string.Join(
        Environment.NewLine,
        logs.Logs.Select(log => string.Join(
            " ",
            new[] { log.Category, log.Message }
                .Concat(log.State.Values.Select(value => value?.ToString() ?? string.Empty))
                .Concat(log.Scope.Values.Select(value => value?.ToString() ?? string.Empty))
                .Append(log.Exception?.ToString() ?? string.Empty))));

    private static Task<HttpResponseMessage> SendMutationAsync(
        MutationScenario scenario,
        TeamMutationKind kind,
        MutationAttempt attempt)
    {
        var client = attempt == MutationAttempt.MemberDenied
            ? scenario.MemberClient
            : scenario.AdminClient;
        var organizationId = attempt switch
        {
            MutationAttempt.MissingResource when kind == TeamMutationKind.Create =>
                Guid.NewGuid(),
            MutationAttempt.ForeignResource when kind == TeamMutationKind.Create =>
                scenario.ForeignOrganizationId,
            _ => scenario.OrganizationId
        };
        var teamId = attempt switch
        {
            MutationAttempt.MissingResource => Guid.NewGuid(),
            MutationAttempt.ForeignResource => scenario.ForeignTeamId,
            _ => scenario.TeamId
        };
        var targetUserId = attempt switch
        {
            MutationAttempt.MissingMember when kind == TeamMutationKind.AddMember =>
                Guid.NewGuid(),
            MutationAttempt.MissingMember => scenario.NonTeamUserId,
            _ => scenario.TargetUserId
        };
        var teamPath =
            $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}";
        var name = (kind, attempt) switch
        {
            (TeamMutationKind.Create, MutationAttempt.MemberDenied) =>
                "Sensitive Member Create",
            (TeamMutationKind.Update, MutationAttempt.MemberDenied) =>
                "Sensitive Member Update",
            (_, MutationAttempt.AdminSuccess) => "Admin Mutation Team",
            (_, MutationAttempt.MissingResource) => "Missing Resource Team",
            (_, MutationAttempt.ForeignResource) => "Foreign Resource Team",
            _ => "Matrix Mutation Team"
        };

        return kind switch
        {
            TeamMutationKind.Create =>
                OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    client,
                    HttpMethod.Post,
                    $"/api/v1/organizations/{organizationId:D}/teams",
                    new { name }),
            TeamMutationKind.Update =>
                OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    client,
                    HttpMethod.Patch,
                    teamPath,
                    new { name }),
            TeamMutationKind.Delete =>
                OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    client,
                    HttpMethod.Delete,
                    teamPath),
            TeamMutationKind.AddMember =>
                OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    client,
                    HttpMethod.Post,
                    $"{teamPath}/members",
                    new { userId = targetUserId }),
            TeamMutationKind.RemoveMember =>
                OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    client,
                    HttpMethod.Delete,
                    $"{teamPath}/members/{targetUserId:D}"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async Task<MutationScenario> CreateMutationScenarioAsync(
        TeamMutationKind kind)
    {
        var ownerClient = factory.CreateApiClient();
        var adminClient = factory.CreateApiClient();
        var memberClient = factory.CreateApiClient();
        var targetClient = factory.CreateApiClient();
        var nonTeamClient = factory.CreateApiClient();
        var foreignOwnerClient = factory.CreateApiClient();
        try
        {
            await OrganizationEndpointTestSupport.CreateScenarioAsync(
                ownerClient,
                "Matrix Owner",
                $"local-agent+matrix-owner-{kind}@local-agent.test");
            var admin = await OrganizationEndpointTestSupport.CreateScenarioAsync(
                adminClient,
                "Matrix Admin",
                $"local-agent+matrix-admin-{kind}@local-agent.test");
            var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
                memberClient,
                "Matrix Member",
                $"local-agent+matrix-member-{kind}@local-agent.test");
            var target = await OrganizationEndpointTestSupport.CreateScenarioAsync(
                targetClient,
                "Matrix Target",
                $"local-agent+matrix-target-{kind}@local-agent.test");
            var nonTeam = await OrganizationEndpointTestSupport.CreateScenarioAsync(
                nonTeamClient,
                "Matrix Non Team",
                $"local-agent+matrix-non-team-{kind}@local-agent.test");
            await OrganizationEndpointTestSupport.CreateScenarioAsync(
                foreignOwnerClient,
                "Matrix Foreign Owner",
                $"local-agent+matrix-foreign-{kind}@local-agent.test");

            using var organization =
                await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                    ownerClient,
                    $"Matrix Workspace {kind}");
            var organizationId =
                (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
                .GetProperty("id").GetGuid();
            foreach (var (userId, role) in new[]
                     {
                         (admin.UserId, "admin"),
                         (member.UserId, "member"),
                         (target.UserId, "member"),
                         (nonTeam.UserId, "member")
                     })
            {
                using var added =
                    await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                        ownerClient,
                        HttpMethod.Post,
                        $"/api/v1/organizations/{organizationId:D}/members",
                        new { userId, role });
                Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            }

            using var team = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/teams",
                new { name = $"Matrix Team {kind}" });
            Assert.Equal(HttpStatusCode.Created, team.StatusCode);
            var teamId = (await OrganizationEndpointTestSupport.ReadDataAsync(team))
                .GetProperty("id").GetGuid();

            if (kind == TeamMutationKind.RemoveMember)
            {
                using var addedTeamMember =
                    await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                        ownerClient,
                        HttpMethod.Post,
                        $"/api/v1/organizations/{organizationId:D}/teams/{teamId:D}/members",
                        new { userId = target.UserId });
                Assert.Equal(HttpStatusCode.Created, addedTeamMember.StatusCode);
            }

            using var foreignOrganization =
                await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                    foreignOwnerClient,
                    $"Matrix Foreign Workspace {kind}");
            var foreignOrganizationId =
                (await OrganizationEndpointTestSupport.ReadDataAsync(
                    foreignOrganization)).GetProperty("id").GetGuid();
            using var foreignTeam =
                await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    foreignOwnerClient,
                    HttpMethod.Post,
                    $"/api/v1/organizations/{foreignOrganizationId:D}/teams",
                    new { name = $"Matrix Foreign Team {kind}" });
            Assert.Equal(HttpStatusCode.Created, foreignTeam.StatusCode);
            var foreignTeamId =
                (await OrganizationEndpointTestSupport.ReadDataAsync(foreignTeam))
                .GetProperty("id").GetGuid();

            return new MutationScenario(
                ownerClient,
                adminClient,
                memberClient,
                targetClient,
                nonTeamClient,
                foreignOwnerClient,
                factory.Services.GetRequiredService<CapturedLogProvider>(),
                organizationId,
                teamId,
                target.UserId,
                nonTeam.UserId,
                foreignOrganizationId,
                foreignTeamId);
        }
        catch
        {
            ownerClient.Dispose();
            adminClient.Dispose();
            memberClient.Dispose();
            targetClient.Dispose();
            nonTeamClient.Dispose();
            foreignOwnerClient.Dispose();
            throw;
        }
    }

    private static async Task AssertMutationProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        CapturedLogProvider logs,
        string operation,
        string? sensitiveToken = null)
    {
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            status,
            code);
        OrganizationEndpointTestSupport.AssertNoStore(response);
        AssertSingleFinalAudit(logs, operation, code);
        if (sensitiveToken is null)
        {
            return;
        }

        Assert.DoesNotContain(
            sensitiveToken,
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            sensitiveToken,
            RenderedLogs(logs),
            StringComparison.Ordinal);
    }

    private static CapturedLog AssertSingleFinalAudit(
        CapturedLogProvider logs,
        string operation,
        string outcome)
    {
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category == CollaborationLogCategory);
        Assert.Equal(LogLevel.Information, audit.Level);
        Assert.Equal(operation, audit.State["CollaborationOperation"]);
        Assert.Equal(outcome, audit.State["CollaborationOutcome"]);
        Assert.IsType<Guid>(audit.State["UserId"]);
        Assert.IsType<Guid>(audit.State["SessionId"]);
        Assert.False(string.IsNullOrWhiteSpace(audit.Scope["TraceId"]?.ToString()));
        Assert.Null(audit.Exception);
        return audit;
    }

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

    private enum FaultProbeMode
    {
        ThrowFromList,
        ThrowDuringCandidateProjection
    }

    private sealed class FaultProbeTeamStore(FaultProbeMode mode) : ITeamStore
    {
        internal int CallCount { get; private set; }

        public Task<TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>> ListAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (mode == FaultProbeMode.ThrowFromList)
            {
                throw new InvalidOperationException("Sensitive store exception");
            }

            throw new NotSupportedException();
        }

        public Task<TeamOperationResult<TeamSummary>> CreateAsync(
            CreateTeamCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamOperationResult<TeamSummary>> UpdateAsync(
            UpdateTeamCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamOperationResult<TeamDeletion>> DeleteAsync(
            DeleteTeamCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>> ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamOperationResult<TeamMemberView>> AddMemberAsync(
            AddTeamMemberCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamOperationResult<TeamMemberRemoval>> RemoveMemberAsync(
            RemoveTeamMemberCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>> ListCandidatesAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamId teamId,
            string? query,
            TeamCandidateCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (mode != FaultProbeMode.ThrowDuringCandidateProjection)
            {
                throw new NotSupportedException();
            }

            return Task.FromResult(TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>.Success(
                new TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>(
                    new ThrowingReadOnlyList<TeamCandidate>(),
                    null)));
        }
    }

    private sealed class ThrowingReadOnlyList<T> : IReadOnlyList<T>
    {
        public int Count => 1;

        public T this[int index] => throw new InvalidOperationException(
            "Sensitive projection exception");

        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException(
            "Sensitive projection exception");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private sealed class RequestBodyDetectionStartupFilter(string path)
        : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, continuation) =>
                {
                    if (string.Equals(
                            context.Request.Path.Value,
                            path,
                            StringComparison.Ordinal))
                    {
                        context.Request.ContentLength = null;
                        context.Request.Headers.Remove("Transfer-Encoding");
                        context.Features.Set<IHttpRequestBodyDetectionFeature>(
                            RequestBodyDetectionFeature.Instance);
                    }

                    await continuation();
                });
                next(app);
            };
    }

    private sealed class RequestBodyDetectionFeature
        : IHttpRequestBodyDetectionFeature
    {
        internal static RequestBodyDetectionFeature Instance { get; } = new();

        public bool CanHaveBody => true;
    }

    public enum TeamMutationKind
    {
        Create,
        Update,
        Delete,
        AddMember,
        RemoveMember
    }

    private enum MutationAttempt
    {
        MemberDenied,
        AdminSuccess,
        MissingResource,
        ForeignResource,
        MissingMember
    }

    private sealed record MutationScenario(
        HttpClient OwnerClient,
        HttpClient AdminClient,
        HttpClient MemberClient,
        HttpClient TargetClient,
        HttpClient NonTeamClient,
        HttpClient ForeignOwnerClient,
        CapturedLogProvider Logs,
        Guid OrganizationId,
        Guid TeamId,
        Guid TargetUserId,
        Guid NonTeamUserId,
        Guid ForeignOrganizationId,
        Guid ForeignTeamId) : IDisposable
    {
        internal string TeamPath =>
            $"/api/v1/organizations/{OrganizationId:D}/teams/{TeamId:D}";

        public void Dispose()
        {
            OwnerClient.Dispose();
            AdminClient.Dispose();
            MemberClient.Dispose();
            TargetClient.Dispose();
            NonTeamClient.Dispose();
            ForeignOwnerClient.Dispose();
        }
    }
}
