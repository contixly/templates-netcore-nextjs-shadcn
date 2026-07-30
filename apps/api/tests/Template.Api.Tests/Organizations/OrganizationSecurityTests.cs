using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Observability;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationSecurityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static TheoryData<string, string, bool> ProtectedRoutes =>
        new()
        {
            { "GET", "/api/v1/organizations", false },
            { "POST", "/api/v1/organizations", true },
            {
                "GET",
                "/api/v1/organizations/by-key/example-workspace",
                false
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701",
                true
            },
            {
                "DELETE",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701",
                true
            },
            {
                "PUT",
                "/api/v1/auth/session/active-organization",
                true
            },
            {
                "GET",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members",
                false
            },
            {
                "POST",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members",
                true
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members/0198a7ac-d0f8-7832-b711-211f56c57702",
                true
            }
        };

    public static TheoryData<string, string, string> MutationBodies =>
        new()
        {
            {
                "POST",
                "/api/v1/organizations",
                """{"name":"CSRF Workspace"}"""
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701",
                """{"name":"CSRF Workspace"}"""
            },
            {
                "DELETE",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701",
                """{"confirmationName":"CSRF Workspace"}"""
            },
            {
                "PUT",
                "/api/v1/auth/session/active-organization",
                """{"organizationId":"0198a7ac-d0f8-7832-b711-211f56c57701"}"""
            },
            {
                "POST",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members",
                """{"userId":"0198a7ac-d0f8-7832-b711-211f56c57702","role":"member"}"""
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members/0198a7ac-d0f8-7832-b711-211f56c57702",
                """{"role":"admin"}"""
            }
        };

    public static TheoryData<string, string, string> UnknownMemberBodies =>
        new()
        {
            {
                "POST",
                "/api/v1/organizations",
                """{"name":"Strict Workspace","unknown":true}"""
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701",
                """{"name":"Strict Workspace","unknown":true}"""
            },
            {
                "DELETE",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701",
                """{"confirmationName":"Strict Workspace","unknown":true}"""
            },
            {
                "PUT",
                "/api/v1/auth/session/active-organization",
                """{"organizationId":"0198a7ac-d0f8-7832-b711-211f56c57701","unknown":true}"""
            },
            {
                "POST",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members",
                """{"userId":"0198a7ac-d0f8-7832-b711-211f56c57702","role":"member","unknown":true}"""
            },
            {
                "PATCH",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members/0198a7ac-d0f8-7832-b711-211f56c57702",
                """{"role":"admin","unknown":true}"""
            }
        };

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task EveryOrganizationRouteRequiresBrowserSessionAndIsNeverCached(
        string method,
        string path,
        bool hasBody)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (hasBody)
        {
            request.Content = JsonContent.Create(new { });
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
    public async Task EveryOrganizationMutationRequiresCorrectAntiforgery(
        string method,
        string path,
        string body)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "CSRF Owner",
            "local-agent+organization-csrf@local-agent.test");

        using var missingRequest = new HttpRequestMessage(
            new HttpMethod(method),
            path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        using var missing = await client.SendAsync(
            missingRequest,
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            missing,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var wrongRequest = new HttpRequestMessage(
            new HttpMethod(method),
            path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        wrongRequest.Headers.Add("X-CSRF-TOKEN", "wrong-request-token");
        using var wrong = await client.SendAsync(
            wrongRequest,
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            wrong,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
        OrganizationEndpointTestSupport.AssertNoStore(missing, wrong);
    }

    [Theory]
    [MemberData(nameof(MutationBodies))]
    public async Task EveryOrganizationMutationRejectsMalformedAndNonJsonBodies(
        string method,
        string path,
        string _)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict JSON Owner",
            "local-agent+organization-strict-json@local-agent.test");

        using var malformed =
            await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                client,
                new HttpMethod(method),
                path,
                """{"broken":""",
                "application/json");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            malformed,
            HttpStatusCode.BadRequest,
            "invalid_request");

        using var nonJson =
            await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                client,
                new HttpMethod(method),
                path,
                """{"name":"Not JSON"}""",
                "text/plain");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            nonJson,
            HttpStatusCode.BadRequest,
            "invalid_request");
        OrganizationEndpointTestSupport.AssertNoStore(malformed, nonJson);
    }

    [Theory]
    [MemberData(nameof(UnknownMemberBodies))]
    public async Task EveryOrganizationMutationRejectsUnknownJsonMembers(
        string method,
        string path,
        string body)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Unknown JSON Owner",
            "local-agent+organization-unknown-json@local-agent.test");

        using var response =
            await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                client,
                new HttpMethod(method),
                path,
                body,
                "application/json");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Fact]
    public async Task ForeignResourcesAreNotDisclosedAndMemberRoleDenialsPrecedeTargetState()
    {
        using var ownerClient = factory.CreateApiClient();
        var owner = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Authorization Owner",
            "local-agent+authorization-owner@local-agent.test");
        using var memberClient = factory.CreateApiClient();
        var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            memberClient,
            "Authorization Member",
            "local-agent+authorization-member@local-agent.test");
        using var foreignClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            foreignClient,
            "Authorization Foreign",
            "local-agent+authorization-foreign@local-agent.test");
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Authorization Workspace");
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();
        using var addMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = member.UserId, role = "member" });
        Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);

        using var deniedUpdate =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                memberClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new
                {
                    name = "Authorization Denied Rename",
                    slug = "authorization-denied-rename"
                });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            deniedUpdate,
            HttpStatusCode.Forbidden,
            "organization_permission_denied");
        using var deniedDelete =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                memberClient,
                HttpMethod.Delete,
                $"/api/v1/organizations/{organizationId:D}",
                new { confirmationName = "Wrong Confirmation Must Not Win" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            deniedDelete,
            HttpStatusCode.Forbidden,
            "organization_permission_denied");
        using var deniedAdd =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                memberClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = owner.UserId, role = "member" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            deniedAdd,
            HttpStatusCode.Forbidden,
            "role_assignment_forbidden");
        using var deniedRole =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                memberClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{Guid.NewGuid():D}",
                new { role = "admin" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            deniedRole,
            HttpStatusCode.Forbidden,
            "role_assignment_forbidden");

        var foreignCases = new[]
        {
            new ForeignCase(
                HttpMethod.Get,
                $"/api/v1/organizations/by-key/{organizationId:D}",
                Body: null),
            new ForeignCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new { name = "Foreign Rename" }),
            new ForeignCase(
                HttpMethod.Delete,
                $"/api/v1/organizations/{organizationId:D}",
                new { confirmationName = "Authorization Workspace" }),
            new ForeignCase(
                HttpMethod.Put,
                "/api/v1/auth/session/active-organization",
                new { organizationId }),
            new ForeignCase(
                HttpMethod.Get,
                $"/api/v1/organizations/{organizationId:D}/members",
                Body: null),
            new ForeignCase(
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = owner.UserId, role = "member" }),
            new ForeignCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{Guid.NewGuid():D}",
                new { role = "member" })
        };
        foreach (var foreignCase in foreignCases)
        {
            using var response = foreignCase.Body is null
                ? await foreignClient.SendAsync(
                    new HttpRequestMessage(
                        foreignCase.Method,
                        foreignCase.Path),
                    TestContext.Current.CancellationToken)
                : await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                    foreignClient,
                    foreignCase.Method,
                    foreignCase.Path,
                    foreignCase.Body);
            await OrganizationEndpointTestSupport.AssertProblemAsync(
                response,
                HttpStatusCode.NotFound,
                "organization_not_found");
            OrganizationEndpointTestSupport.AssertNoStore(response);
        }
    }

    [Fact]
    public async Task MemberDeleteRouteDoesNotExist()
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "No Member Delete Owner",
            "local-agent+no-member-delete@local-agent.test");
        using var response = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/organizations/{Guid.NewGuid():D}/members/{Guid.NewGuid():D}");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.MethodNotAllowed,
            "method_not_allowed");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    [Fact]
    public async Task OrganizationSecurityEventsNeverRenderNamesEmailsDomainsOrBodies()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Safe Audit Owner",
            "local-agent+safe-audit-owner@local-agent.test");
        using var targetClient = factory.CreateApiClient();
        var target = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            targetClient,
            "Sensitive Target Name 991",
            "local-agent+sensitive-target-991@local-agent.test");
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Sensitive Organization Name 991");
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();
        using var update = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Patch,
            $"/api/v1/organizations/{organizationId:D}",
            new
            {
                name = "Sensitive Organization Renamed 992",
                allowedEmailDomains = new[] { "sensitive-domain-992.example" }
            });
        using var warning = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new { userId = target.UserId, role = "member" });
        using var detail = await ownerClient.GetAsync(
            "/api/v1/organizations/by-key/sensitive-organization-name-991",
            TestContext.Current.CancellationToken);
        using var fault = await ownerClient.GetAsync(
            "/api/testing/fault/by-key/sensitive-organization-name-991",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, warning.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, fault.StatusCode);
        var events = logs.Logs
            .Where(log =>
                log.Category ==
                "Template.Api.Features.Organizations.OrganizationEndpointModule")
            .ToArray();
        Assert.NotEmpty(events);
        Assert.All(
            events,
            log =>
            {
                Assert.True(log.State.ContainsKey("OrganizationOperation"));
                Assert.True(log.State.ContainsKey("OrganizationOutcome"));
                Assert.True(log.State.ContainsKey("UserId"));
                Assert.True(log.State.ContainsKey("SessionId"));
                Assert.True(log.Scope.ContainsKey("TraceId"));
            });
        var rendered = string.Join(
            Environment.NewLine,
            events.Select(log => log.Message));
        Assert.DoesNotContain("Sensitive Organization", rendered);
        Assert.DoesNotContain(target.Email, rendered);
        Assert.DoesNotContain("sensitive-domain-992.example", rendered);
        Assert.DoesNotContain("acknowledgeDomainRestriction", rendered);
        Assert.DoesNotContain("__Host-template", rendered);

        var genericRendered = string.Join(
            Environment.NewLine,
            logs.Logs
                .Where(log =>
                    log.Category.EndsWith(
                        nameof(RequestLoggingMiddleware),
                        StringComparison.Ordinal) ||
                    log.Category.EndsWith(
                        "ApiExceptionHandler",
                        StringComparison.Ordinal))
                .Select(log =>
                    string.Join(
                        " ",
                        new[] { log.Message }.Concat(
                            log.State.Values.Select(value =>
                                value?.ToString() ?? string.Empty)))));
        Assert.DoesNotContain(
            "sensitive-organization-name-991",
            genericRendered,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ForeignCase(
        HttpMethod Method,
        string Path,
        object? Body);
}
