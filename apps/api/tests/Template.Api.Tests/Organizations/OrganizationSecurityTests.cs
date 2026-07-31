using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Template.Api.Observability;
using Template.Api.Tests.Infrastructure;
using Template.Application.Organizations;
using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationSecurityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private const string OrganizationLogCategory =
        "Template.Api.Features.Organizations.OrganizationEndpointModule";

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
            { "GET", "/api/v1/organizations?limit=not-a-number", false },
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
                "GET",
                "/api/v1/organizations/0198a7ac-d0f8-7832-b711-211f56c57701/members?limit=999999999999999999999999999999",
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

    public static TheoryData<string, string, HttpStatusCode, string>
        CompleteLogSafetyCases =>
        new()
        {
            {
                "/api/v1/organizations/round13-sensitive-invalid-route/members",
                "round13-sensitive-invalid-route",
                HttpStatusCode.BadRequest,
                "validation_failed"
            },
            {
                "/api/v1/organizations/by-key/round13-sensitive-name-derived",
                "round13-sensitive-name-derived",
                HttpStatusCode.NotFound,
                "organization_not_found"
            },
            {
                "/api/v1/organizations?limit=0",
                "limit=0",
                HttpStatusCode.BadRequest,
                "validation_failed"
            },
            {
                "/api/v1/organizations?cursor=round13-sensitive-cursor",
                "round13-sensitive-cursor",
                HttpStatusCode.BadRequest,
                "invalid_cursor"
            }
        };

    [Theory]
    [MemberData(nameof(CompleteLogSafetyCases))]
    public async Task CompleteCapturedLogsExcludeRawOrganizationRouteAndQueryValues(
        string path,
        string sensitiveToken,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Round 13 Complete Log Actor",
            $"local-agent+round13-complete-{Guid.NewGuid():N}@local-agent.test");
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            expectedStatus,
            expectedCode);
        Assert.DoesNotContain(
            logs.Logs,
            log => log.Scope.Values.Any(value =>
                value?.ToString()?.Contains(
                    sensitiveToken,
                    StringComparison.OrdinalIgnoreCase) is true));
        Assert.DoesNotContain(
            logs.Logs,
            log => RenderedLog(log).Contains(
                sensitiveToken,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InvalidOrganizationKeysAreNonDisclosingAuditedAndNeverReachTheStore()
    {
        var (isolated, client, store, actor, sessionId, logs) =
            await CreateBoundaryProbeAsync();
        await using (isolated)
        using (client)
        {
            using var signIn = await LocalAuthTestClient.SignInAsync(
                client,
                actor.Email,
                actor.Password);
            Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
            var sessionCookie = signIn.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith(
                    "__Host-template.session=",
                    StringComparison.Ordinal));
            var cookiePair = sessionCookie[..sessionCookie.IndexOf(';')];
            sessionId = await ReadSessionIdAsync(client);

            logs.Clear();
            var nulContext = await isolated.Server.SendAsync(
                http =>
                {
                    http.Request.Method = HttpMethod.Get.Method;
                    http.Request.Scheme = "https";
                    http.Request.Host = new HostString("localhost");
                    http.Request.Path =
                        new PathString("/api/v1/organizations/by-key/\0");
                    http.Request.Headers.Cookie = cookiePair;
                },
                TestContext.Current.CancellationToken);

            Assert.Equal(StatusCodes.Status404NotFound, nulContext.Response.StatusCode);
            AssertSingleOrganizationAudit(
                logs,
                "organization_get",
                "organization_not_found",
                actor.UserId,
                sessionId,
                organizationId: null,
                memberId: null);
            Assert.DoesNotContain('\0', RenderedLogs(logs));

            logs.Clear();
            const string invalidMarker = "round17-sensitive.invalid";
            using var markerResponse = await client.GetAsync(
                $"/api/v1/organizations/by-key/{invalidMarker}",
                TestContext.Current.CancellationToken);

            await OrganizationEndpointTestSupport.AssertProblemAsync(
                markerResponse,
                HttpStatusCode.NotFound,
                "organization_not_found");
            OrganizationEndpointTestSupport.AssertNoStore(markerResponse);
            AssertSingleOrganizationAudit(
                logs,
                "organization_get",
                "organization_not_found",
                actor.UserId,
                sessionId,
                organizationId: null,
                memberId: null);
            Assert.DoesNotContain(
                invalidMarker,
                RenderedLogs(logs),
                StringComparison.OrdinalIgnoreCase);

            Assert.Equal(0, store.CallCount);
        }
    }

    [Fact]
    public async Task WhitespaceWrappedOrganizationUuidKeysAreAuditedAndNeverReachTheStore()
    {
        var organizationId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701");
        var canonicalKey = organizationId.ToString("D");
        var encodedKeys = new[]
        {
            $"%20{canonicalKey}",
            $"{canonicalKey}%20",
            $"%20{canonicalKey}%20",
            $"%09{canonicalKey}%09"
        };
        var (isolated, client, store, actor, sessionId, logs) =
            await CreateBoundaryProbeAsync();
        await using (isolated)
        using (client)
        {
            foreach (var encodedKey in encodedKeys)
            {
                logs.Clear();
                using var response = await client.GetAsync(
                    $"/api/v1/organizations/by-key/{encodedKey}",
                    TestContext.Current.CancellationToken);

                await OrganizationEndpointTestSupport.AssertProblemAsync(
                    response,
                    HttpStatusCode.NotFound,
                    "organization_not_found");
                OrganizationEndpointTestSupport.AssertNoStore(response);
                AssertSingleOrganizationAudit(
                    logs,
                    "organization_get",
                    "organization_not_found",
                    actor.UserId,
                    sessionId,
                    organizationId: null,
                    memberId: null);
                var rendered = RenderedLogs(logs);
                Assert.DoesNotContain(
                    canonicalKey,
                    rendered,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    encodedKey,
                    rendered,
                    StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, store.CallCount);
        }
    }

    [Fact]
    public async Task CanonicalOrganizationUuidKeysAcceptPublishedHexCasing()
    {
        using var client = factory.CreateApiClient();
        var actor = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Canonical UUID Owner",
            $"local-agent+canonical-uuid-{Guid.NewGuid():N}@local-agent.test");
        var sessionId = await ReadSessionIdAsync(client);
        using var created =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Canonical UUID Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(created))
            .GetProperty("id")
            .GetGuid();
        var uppercaseKey = organizationId.ToString("D").ToUpperInvariant();
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await client.GetAsync(
            $"/api/v1/organizations/by-key/{uppercaseKey}",
            TestContext.Current.CancellationToken);
        var data = await OrganizationEndpointTestSupport.ReadDataAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(organizationId, data.GetProperty("id").GetGuid());
        OrganizationEndpointTestSupport.AssertNoStore(response);
        AssertSingleOrganizationAudit(
            logs,
            "organization_get",
            "succeeded",
            actor.UserId,
            sessionId,
            organizationId,
            memberId: null);
    }

    [Fact]
    public async Task EveryOrganizationMutationAuditsMalformedJsonAtTheAuthenticatedBoundary()
    {
        var organizationId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701");
        var memberId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702");
        var cases = new[]
        {
            new BoundaryCase(
                HttpMethod.Post,
                "/api/v1/organizations",
                "organization_create",
                "{\"round13-sensitive-create\":",
                ExpectedOrganizationId: null,
                ExpectedMemberId: null),
            new BoundaryCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                "organization_update",
                "{\"round13-sensitive-update\":",
                organizationId,
                ExpectedMemberId: null),
            new BoundaryCase(
                HttpMethod.Delete,
                $"/api/v1/organizations/{organizationId:D}",
                "organization_delete",
                "{\"round13-sensitive-delete\":",
                organizationId,
                ExpectedMemberId: null),
            new BoundaryCase(
                HttpMethod.Put,
                "/api/v1/auth/session/active-organization",
                "active_organization_set",
                "{\"round13-sensitive-active\":",
                ExpectedOrganizationId: null,
                ExpectedMemberId: null),
            new BoundaryCase(
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                "organization_member_add",
                "{\"round13-sensitive-member-add\":",
                organizationId,
                ExpectedMemberId: null),
            new BoundaryCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
                "organization_member_role_update",
                "{\"round13-sensitive-role-update\":",
                organizationId,
                memberId)
        };
        var (isolated, client, store, actor, sessionId, logs) =
            await CreateBoundaryProbeAsync();
        await using (isolated)
        using (client)
        {
            foreach (var boundaryCase in cases)
            {
                logs.Clear();
                using var response =
                    await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                        client,
                        boundaryCase.Method,
                        boundaryCase.Path,
                        boundaryCase.Body!,
                        "application/json");

                await OrganizationEndpointTestSupport.AssertProblemAsync(
                    response,
                    HttpStatusCode.BadRequest,
                    "invalid_request");
                OrganizationEndpointTestSupport.AssertNoStore(response);
                AssertSingleOrganizationAudit(
                    logs,
                    boundaryCase.Operation,
                    "invalid_request",
                    actor.UserId,
                    sessionId,
                    boundaryCase.ExpectedOrganizationId,
                    boundaryCase.ExpectedMemberId);
                Assert.DoesNotContain(
                    boundaryCase.SensitiveValue,
                    RenderedLogs(logs),
                    StringComparison.Ordinal);
            }

            Assert.Equal(0, store.CallCount);
        }
    }

    [Fact]
    public async Task EveryOrganizationMutationAuditsInvalidFieldsWithoutRenderingBodyValues()
    {
        var organizationId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701");
        var memberId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702");
        var targetUserId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57703");
        var invalidConfirmation = new string('Q', 51);
        var cases = new[]
        {
            new BoundaryCase(
                HttpMethod.Post,
                "/api/v1/organizations",
                "organization_create",
                $$"""{"name":"{{new string('N', 51)}}"}""",
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: new string('N', 51)),
            new BoundaryCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                "organization_update",
                """{"slug":"round13_sensitive_invalid_slug"}""",
                organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "round13_sensitive_invalid_slug"),
            new BoundaryCase(
                HttpMethod.Delete,
                $"/api/v1/organizations/{organizationId:D}",
                "organization_delete",
                $$"""{"confirmationName":"{{invalidConfirmation}}"}""",
                organizationId,
                ExpectedMemberId: null,
                SensitiveToken: invalidConfirmation),
            new BoundaryCase(
                HttpMethod.Put,
                "/api/v1/auth/session/active-organization",
                "active_organization_set",
                """{"organizationId":"00000000-0000-0000-0000-000000000000"}""",
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "00000000-0000-0000-0000-000000000000"),
            new BoundaryCase(
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                "organization_member_add",
                $$"""{"userId":"{{targetUserId:D}}","role":"round13-sensitive-role-add"}""",
                organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "round13-sensitive-role-add"),
            new BoundaryCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
                "organization_member_role_update",
                """{"role":"round13-sensitive-role-update"}""",
                organizationId,
                memberId,
                SensitiveToken: "round13-sensitive-role-update")
        };
        var (isolated, client, store, actor, sessionId, logs) =
            await CreateBoundaryProbeAsync();
        await using (isolated)
        using (client)
        {
            foreach (var boundaryCase in cases)
            {
                logs.Clear();
                using var response =
                    await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                        client,
                        boundaryCase.Method,
                        boundaryCase.Path,
                        boundaryCase.Body!,
                        "application/json");

                await OrganizationEndpointTestSupport.AssertProblemAsync(
                    response,
                    HttpStatusCode.BadRequest,
                    "validation_failed");
                OrganizationEndpointTestSupport.AssertNoStore(response);
                AssertSingleOrganizationAudit(
                    logs,
                    boundaryCase.Operation,
                    "validation_failed",
                    actor.UserId,
                    sessionId,
                    boundaryCase.ExpectedOrganizationId,
                    boundaryCase.ExpectedMemberId);
                var rendered = RenderedLogs(logs);
                Assert.DoesNotContain(
                    boundaryCase.SensitiveValue,
                    rendered,
                    StringComparison.Ordinal);
                if (boundaryCase.Operation == "organization_member_add")
                {
                    Assert.DoesNotContain(
                        targetUserId.ToString("D"),
                        rendered,
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.Equal(0, store.CallCount);
        }
    }

    [Fact]
    public async Task RouteQueryAndCursorRejectionsAuditOnlySafeOpaqueIdentifiers()
    {
        var organizationId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57701");
        var memberId = Guid.Parse("0198a7ac-d0f8-7832-b711-211f56c57702");
        var cases = new[]
        {
            new BoundaryCase(
                HttpMethod.Patch,
                "/api/v1/organizations/round13-sensitive-invalid-organization",
                "organization_update",
                """{"name":"Valid Boundary Name"}""",
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "round13-sensitive-invalid-organization"),
            new BoundaryCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/round13-sensitive-invalid-organization/members/{memberId:D}",
                "organization_member_role_update",
                """{"role":"member"}""",
                ExpectedOrganizationId: null,
                ExpectedMemberId: memberId,
                SensitiveToken: "round13-sensitive-invalid-organization"),
            new BoundaryCase(
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/round13-sensitive-invalid-member",
                "organization_member_role_update",
                """{"role":"member"}""",
                ExpectedOrganizationId: organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "round13-sensitive-invalid-member"),
            new BoundaryCase(
                HttpMethod.Get,
                "/api/v1/organizations/round13-sensitive-list-id/members",
                "organization_members_list",
                Body: null,
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "round13-sensitive-list-id",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                "/api/v1/organizations?limit=0",
                "organization_list",
                Body: null,
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "limit=0",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                "/api/v1/organizations?limit=101",
                "organization_list",
                Body: null,
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "limit=101",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                "/api/v1/organizations?limit=not-a-number",
                "organization_list",
                Body: null,
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "limit=not-a-number",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                "/api/v1/organizations?limit=999999999999999999999999999999",
                "organization_list",
                Body: null,
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "limit=999999999999999999999999999999",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                $"/api/v1/organizations/{organizationId:D}/members?limit=0",
                "organization_members_list",
                Body: null,
                ExpectedOrganizationId: organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "limit=0",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                $"/api/v1/organizations/{organizationId:D}/members?limit=101",
                "organization_members_list",
                Body: null,
                ExpectedOrganizationId: organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "limit=101",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                $"/api/v1/organizations/{organizationId:D}/members?limit=not-a-number",
                "organization_members_list",
                Body: null,
                ExpectedOrganizationId: organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "limit=not-a-number",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                $"/api/v1/organizations/{organizationId:D}/members?limit=999999999999999999999999999999",
                "organization_members_list",
                Body: null,
                ExpectedOrganizationId: organizationId,
                ExpectedMemberId: null,
                SensitiveToken: "limit=999999999999999999999999999999",
                RequiresCsrf: false),
            new BoundaryCase(
                HttpMethod.Get,
                "/api/v1/organizations?cursor=round13-sensitive-cursor",
                "organization_list",
                Body: null,
                ExpectedOrganizationId: null,
                ExpectedMemberId: null,
                SensitiveToken: "round13-sensitive-cursor",
                RequiresCsrf: false,
                ExpectedCode: "invalid_cursor")
        };
        var (isolated, client, store, actor, sessionId, logs) =
            await CreateBoundaryProbeAsync();
        await using (isolated)
        using (client)
        {
            foreach (var boundaryCase in cases)
            {
                logs.Clear();
                using var response = boundaryCase.RequiresCsrf
                    ? await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
                        client,
                        boundaryCase.Method,
                        boundaryCase.Path,
                        boundaryCase.Body!,
                        "application/json")
                    : await client.SendAsync(
                        new HttpRequestMessage(
                            boundaryCase.Method,
                            boundaryCase.Path),
                        TestContext.Current.CancellationToken);
                var expectedCode = boundaryCase.ExpectedCode ?? "validation_failed";

                await OrganizationEndpointTestSupport.AssertProblemAsync(
                    response,
                    HttpStatusCode.BadRequest,
                    expectedCode);
                OrganizationEndpointTestSupport.AssertNoStore(response);
                AssertSingleOrganizationAudit(
                    logs,
                    boundaryCase.Operation,
                    expectedCode,
                    actor.UserId,
                    sessionId,
                    boundaryCase.ExpectedOrganizationId,
                    boundaryCase.ExpectedMemberId);
                Assert.DoesNotContain(
                    boundaryCase.SensitiveValue,
                    RenderedLogs(logs),
                    StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, store.CallCount);
        }
    }

    [Fact]
    public async Task EveryOrganizationMutationSuccessAndBusinessFailureEmitsExactlyOneAudit()
    {
        using var ownerClient = factory.CreateApiClient();
        var owner = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Round 13 Audit Owner",
            "local-agent+round13-audit-owner@local-agent.test");
        var ownerSessionId = await ReadSessionIdAsync(ownerClient);
        using var targetClient = factory.CreateApiClient();
        var target = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            targetClient,
            "Round 13 Audit Target",
            "local-agent+round13-audit-target@local-agent.test");
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();

        logs.Clear();
        using var create = await OrganizationEndpointTestSupport.CreateOrganizationAsync(
            ownerClient,
            "Round 13 Primary");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var organizationId = (await OrganizationEndpointTestSupport.ReadDataAsync(create))
            .GetProperty("id")
            .GetGuid();
        AssertSingleOrganizationAudit(
            logs,
            "organization_create",
            "succeeded",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId: null);

        logs.Clear();
        using var duplicateCreate =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Round 13 Primary");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            duplicateCreate,
            HttpStatusCode.Conflict,
            "organization_name_conflict");
        AssertSingleOrganizationAudit(
            logs,
            "organization_create",
            "organization_name_conflict",
            owner.UserId,
            ownerSessionId,
            organizationId: null,
            memberId: null);

        using var secondary =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Round 13 Secondary");
        var secondaryId = (await OrganizationEndpointTestSupport.ReadDataAsync(secondary))
            .GetProperty("id")
            .GetGuid();

        logs.Clear();
        using var update = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Patch,
            $"/api/v1/organizations/{organizationId:D}",
            new { name = "Round 13 Primary Renamed" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        AssertSingleOrganizationAudit(
            logs,
            "organization_update",
            "succeeded",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId: null);

        logs.Clear();
        using var conflictingUpdate =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{secondaryId:D}",
                new { name = "Round 13 Primary Renamed" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            conflictingUpdate,
            HttpStatusCode.Conflict,
            "organization_name_conflict");
        AssertSingleOrganizationAudit(
            logs,
            "organization_update",
            "organization_name_conflict",
            owner.UserId,
            ownerSessionId,
            secondaryId,
            memberId: null);

        logs.Clear();
        using var delete = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Delete,
            $"/api/v1/organizations/{secondaryId:D}",
            new { confirmationName = "Round 13 Secondary" });
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        AssertSingleOrganizationAudit(
            logs,
            "organization_delete",
            "succeeded",
            owner.UserId,
            ownerSessionId,
            secondaryId,
            memberId: null);

        logs.Clear();
        using var missingDelete =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Delete,
                $"/api/v1/organizations/{secondaryId:D}",
                new { confirmationName = "Round 13 Secondary" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            missingDelete,
            HttpStatusCode.NotFound,
            "organization_not_found");
        AssertSingleOrganizationAudit(
            logs,
            "organization_delete",
            "organization_not_found",
            owner.UserId,
            ownerSessionId,
            secondaryId,
            memberId: null);

        logs.Clear();
        using var setActive = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Put,
            "/api/v1/auth/session/active-organization",
            new { organizationId });
        Assert.Equal(HttpStatusCode.OK, setActive.StatusCode);
        AssertSingleOrganizationAudit(
            logs,
            "active_organization_set",
            "succeeded",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId: null);

        var foreignOrganizationId = Guid.CreateVersion7();
        logs.Clear();
        using var foreignActive =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Put,
                "/api/v1/auth/session/active-organization",
                new { organizationId = foreignOrganizationId });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            foreignActive,
            HttpStatusCode.NotFound,
            "organization_not_found");
        AssertSingleOrganizationAudit(
            logs,
            "active_organization_set",
            "organization_not_found",
            owner.UserId,
            ownerSessionId,
            foreignOrganizationId,
            memberId: null);

        logs.Clear();
        using var addMember = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            ownerClient,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/members",
            new { userId = target.UserId, role = "member" });
        Assert.Equal(HttpStatusCode.Created, addMember.StatusCode);
        var memberId = (await OrganizationEndpointTestSupport.ReadDataAsync(addMember))
            .GetProperty("id")
            .GetGuid();
        AssertSingleOrganizationAudit(
            logs,
            "organization_member_add",
            "succeeded",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId);

        logs.Clear();
        using var duplicateMember =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = target.UserId, role = "member" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            duplicateMember,
            HttpStatusCode.Conflict,
            "member_already_exists");
        AssertSingleOrganizationAudit(
            logs,
            "organization_member_add",
            "member_already_exists",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId: null);

        logs.Clear();
        using var updateRole =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
                new { role = "admin" });
        Assert.Equal(HttpStatusCode.OK, updateRole.StatusCode);
        AssertSingleOrganizationAudit(
            logs,
            "organization_member_role_update",
            "succeeded",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId);

        logs.Clear();
        using var unchangedRole =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}/members/{memberId:D}",
                new { role = "admin" });
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            unchangedRole,
            HttpStatusCode.Conflict,
            "member_role_unchanged");
        AssertSingleOrganizationAudit(
            logs,
            "organization_member_role_update",
            "member_role_unchanged",
            owner.UserId,
            ownerSessionId,
            organizationId,
            memberId);
    }

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
        Assert.DoesNotContain(
            "sensitive-organization-name-991",
            RenderedLogs(logs),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Equivalent_name_member_conflict_is_stable_non_disclosing_and_not_cached()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Invariant Owner",
            "local-agent+invariant-owner@local-agent.test");
        using var targetClient = factory.CreateApiClient();
        var target = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            targetClient,
            "Sensitive Invariant Target",
            "local-agent+sensitive-invariant-target@local-agent.test");
        using var exactTargetClient = factory.CreateApiClient();
        var exactTarget =
            await OrganizationEndpointTestSupport.CreateScenarioAsync(
                exactTargetClient,
                "Exact Duplicate Target",
                "local-agent+exact-duplicate-target@local-agent.test");
        using var targetOrganization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                targetClient,
                "Sensitive Equivalent Name");
        Assert.Equal(HttpStatusCode.Created, targetOrganization.StatusCode);
        using var receivingOrganization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "sENSITIVE eQUIVALENT nAME");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(
                receivingOrganization))
            .GetProperty("id")
            .GetGuid();

        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var collision =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = target.UserId, role = "member" });
        using var accepted =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = exactTarget.UserId, role = "member" });
        using var exactDuplicate =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId = exactTarget.UserId, role = "member" });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        using var collisionProblem = JsonDocument.Parse(
            await collision.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        using var duplicateProblem = JsonDocument.Parse(
            await exactDuplicate.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, exactDuplicate.StatusCode);
        foreach (var property in new[] { "type", "title", "status", "detail", "code" })
        {
            Assert.Equal(
                duplicateProblem.RootElement.GetProperty(property).GetRawText(),
                collisionProblem.RootElement.GetProperty(property).GetRawText());
        }

        var collisionBody = collisionProblem.RootElement.GetRawText();
        Assert.DoesNotContain(target.Email, collisionBody);
        Assert.DoesNotContain(target.UserId.ToString(), collisionBody);
        Assert.DoesNotContain("Sensitive Equivalent Name", collisionBody);
        Assert.DoesNotContain("emailDomain", collisionBody);
        Assert.DoesNotContain("allowedEmailDomains", collisionBody);
        OrganizationEndpointTestSupport.AssertNoStore(
            collision,
            exactDuplicate);
        Assert.Equal(
            2,
            await OrganizationEndpointTestSupport.CountMembersAsync(
                factory.Services,
                organizationId));

        var renderedLogs = string.Join(
            Environment.NewLine,
            logs.Logs.Select(log =>
                string.Join(
                    " ",
                    new[] { log.Category, log.Message }.Concat(
                        log.State.Values.Select(value =>
                            value?.ToString() ?? string.Empty)))));
        Assert.DoesNotContain(target.Email, renderedLogs);
        Assert.DoesNotContain(target.UserId.ToString(), renderedLogs);
        Assert.DoesNotContain("Sensitive Equivalent Name", renderedLogs);
    }

    private async Task<BoundaryProbeContext> CreateBoundaryProbeAsync()
    {
        var store = new BoundaryProbeOrganizationStore();
        var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrganizationStore>();
                services.AddSingleton<IOrganizationStore>(store);
            }));
        var client = isolated.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        var actor = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Round 13 Boundary Actor",
            $"local-agent+round13-boundary-{Guid.NewGuid():N}@local-agent.test");
        var sessionId = await ReadSessionIdAsync(client);
        var logs = isolated.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        return new BoundaryProbeContext(
            isolated,
            client,
            store,
            actor,
            sessionId,
            logs);
    }

    private static async Task<Guid> ReadSessionIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        return document.RootElement
            .GetProperty("data")
            .GetProperty("session")
            .GetProperty("id")
            .GetGuid();
    }

    private static void AssertSingleOrganizationAudit(
        CapturedLogProvider logs,
        string operation,
        string outcome,
        Guid userId,
        Guid sessionId,
        Guid? organizationId,
        Guid? memberId)
    {
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category == OrganizationLogCategory);
        Assert.Equal(LogLevel.Information, audit.Level);
        Assert.Equal(operation, audit.State["OrganizationOperation"]);
        Assert.Equal(outcome, audit.State["OrganizationOutcome"]);
        Assert.Equal(userId, audit.State["UserId"]);
        Assert.Equal(sessionId, audit.State["SessionId"]);
        Assert.Equal(organizationId, audit.State["OrganizationId"] as Guid?);
        Assert.Equal(memberId, audit.State["MemberId"] as Guid?);
        Assert.False(string.IsNullOrWhiteSpace(audit.Scope["TraceId"]?.ToString()));
        Assert.Null(audit.Exception);
    }

    private static string RenderedLogs(CapturedLogProvider logs) =>
        string.Join(
            Environment.NewLine,
            logs.Logs.Select(RenderedLog));

    private static string RenderedLog(CapturedLog log) =>
        string.Join(
            " ",
            new[] { log.Category, log.Message }.Concat(
                log.State.Values.Select(value =>
                    value?.ToString() ?? string.Empty))
                .Concat(log.Scope.Values.Select(value =>
                    value?.ToString() ?? string.Empty))
                .Append(log.Exception?.ToString() ?? string.Empty));

    private sealed record BoundaryProbeContext(
        WebApplicationFactory<Program> Isolated,
        HttpClient Client,
        BoundaryProbeOrganizationStore Store,
        OrganizationEndpointTestSupport.TestScenario Actor,
        Guid SessionId,
        CapturedLogProvider Logs);

    private sealed record BoundaryCase(
        HttpMethod Method,
        string Path,
        string Operation,
        string? Body,
        Guid? ExpectedOrganizationId,
        Guid? ExpectedMemberId,
        string? SensitiveToken = null,
        bool RequiresCsrf = true,
        string? ExpectedCode = null)
    {
        internal string SensitiveValue { get; } =
            SensitiveToken ?? Body ?? Path;
    }

    private sealed class BoundaryProbeOrganizationStore : IOrganizationStore
    {
        public int CallCount { get; private set; }

        public Task<OrganizationStorePage<
            OrganizationSummary,
            OrganizationListCursorPosition>> ListAsync(
            UserId actorUserId,
            OrganizationListCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) => Reached<
                OrganizationStorePage<
                    OrganizationSummary,
                    OrganizationListCursorPosition>>();

        public Task<OrganizationOperationResult<OrganizationDetail>> GetByKeyAsync(
            UserId actorUserId,
            string organizationKey,
            CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<OrganizationDetail>>();

        public Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
            CreateOrganizationCommand command,
            CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<OrganizationDetail>>();

        public Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
            UpdateOrganizationCommand command,
            CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<OrganizationDetail>>();

        public Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
            DeleteOrganizationCommand command,
            CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<OrganizationDeletion>>();

        public Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
            SetActiveOrganizationCommand command,
            CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<ActiveOrganization>>();

        public Task<OrganizationOperationResult<
            OrganizationStorePage<
                OrganizationMember,
                OrganizationMemberCursorPosition>>> ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken) => Reached<
                OrganizationOperationResult<
                    OrganizationStorePage<
                        OrganizationMember,
                        OrganizationMemberCursorPosition>>>();

        public Task<OrganizationOperationResult<OrganizationMember>> AddMemberAsync(
            AddOrganizationMemberCommand command,
            CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<OrganizationMember>>();

        public Task<OrganizationOperationResult<OrganizationMember>>
            UpdateMemberRoleAsync(
                UpdateOrganizationMemberRoleCommand command,
                CancellationToken cancellationToken) =>
            Reached<OrganizationOperationResult<OrganizationMember>>();

        private Task<T> Reached<T>()
        {
            CallCount++;
            throw new InvalidOperationException(
                "Organization store must not be reached by boundary rejection tests.");
        }
    }

    private sealed record ForeignCase(
        HttpMethod Method,
        string Path,
        object? Body);
}
