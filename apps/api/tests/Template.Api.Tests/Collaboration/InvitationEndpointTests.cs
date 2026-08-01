using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;
using Template.Infrastructure.Collaboration;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Collaboration;

public sealed class InvitationEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private const string AuditCategory =
        "Template.Api.Features.Collaboration.InvitationEndpointModule";

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task InvitationLifecycleUsesExactRoutesStatesLocationsAndNoStore()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Invitation Owner",
            "local-agent+invitation-owner@local-agent.test");
        using var recipientClient = factory.CreateApiClient();
        var recipient = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            recipientClient,
            "Invitation Recipient",
            "local-agent+invitation-recipient@local-agent.test");
        using var rejectedClient = factory.CreateApiClient();
        var rejectedRecipient =
            await OrganizationEndpointTestSupport.CreateScenarioAsync(
                rejectedClient,
                "Rejected Recipient",
                "local-agent+invitation-rejected@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Invitation Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var create = await SendCreateAsync(
            ownerClient,
            organizationId,
            recipient.Email,
            "member");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await OrganizationEndpointTestSupport.ReadDataAsync(create);
        var invitationId = created.GetProperty("id").GetGuid();
        Assert.Equal(
            $"/api/v1/invitations/{invitationId:D}",
            create.Headers.Location?.OriginalString);
        AssertInvitation(
            created,
            invitationId,
            organizationId,
            recipient.Email,
            "member",
            "pending",
            "pending");

        using var duplicate = await SendCreateAsync(
            ownerClient,
            organizationId,
            recipient.Email,
            "member");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            duplicate,
            HttpStatusCode.Conflict,
            "invitation_already_exists");

        using var mismatch = await ownerClient.GetAsync(
            $"/api/v1/invitations/{invitationId:D}",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            mismatch,
            HttpStatusCode.Forbidden,
            "invitation_recipient_mismatch");
        var mismatchBody = await mismatch.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(recipient.Email, mismatchBody, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invitation Workspace",
            mismatchBody,
            StringComparison.Ordinal);

        using var unverified = await recipientClient.GetAsync(
            $"/api/v1/invitations/{invitationId:D}",
            TestContext.Current.CancellationToken);
        var unverifiedDecision =
            await OrganizationEndpointTestSupport.ReadDataAsync(unverified);
        Assert.Equal(HttpStatusCode.OK, unverified.StatusCode);
        Assert.Equal(
            "email-verification-required",
            unverifiedDecision.GetProperty("state").GetString());
        Assert.False(unverifiedDecision.GetProperty("canRespond").GetBoolean());
        Assert.Equal(
            invitationId,
            unverifiedDecision.GetProperty("invitation").GetProperty("id")
                .GetGuid());

        using var account = await recipientClient.GetAsync(
            "/api/v1/account/invitations?limit=50",
            TestContext.Current.CancellationToken);
        var accountPage = await OrganizationEndpointTestSupport.ReadDataAsync(account);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        Assert.Equal(
            invitationId,
            Assert.Single(accountPage.GetProperty("items").EnumerateArray())
                .GetProperty("id").GetGuid());

        using var unverifiedAccept =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                recipientClient,
                HttpMethod.Post,
                $"/api/v1/invitations/{invitationId:D}/accept");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            unverifiedAccept,
            HttpStatusCode.Forbidden,
            "invitation_email_verification_required");
        using var confirm = await LocalAuthTestClient.ConfirmEmailAsync(
            recipientClient);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        using var accept = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            recipientClient,
            HttpMethod.Post,
            $"/api/v1/invitations/{invitationId:D}/accept");
        var accepted = await OrganizationEndpointTestSupport.ReadDataAsync(accept);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(invitationId, accepted.GetProperty("invitationId").GetGuid());
        Assert.Equal(organizationId, accepted.GetProperty("organizationId").GetGuid());
        Assert.Equal(
            "invitation-workspace",
            accepted.GetProperty("canonicalOrganizationKey").GetString());

        using var createRejected = await SendCreateAsync(
            ownerClient,
            organizationId,
            rejectedRecipient.Email,
            "member");
        var rejectedInvitationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(createRejected))
            .GetProperty("id").GetGuid();
        using var confirmRejected = await LocalAuthTestClient.ConfirmEmailAsync(
            rejectedClient);
        using var reject = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
            rejectedClient,
            HttpMethod.Post,
            $"/api/v1/invitations/{rejectedInvitationId:D}/reject");
        var rejected = await OrganizationEndpointTestSupport.ReadDataAsync(reject);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal("rejected", rejected.GetProperty("state").GetString());
        Assert.False(rejected.GetProperty("canRespond").GetBoolean());
        AssertInvitation(
            rejected.GetProperty("invitation"),
            rejectedInvitationId,
            organizationId,
            rejectedRecipient.Email,
            "member",
            "rejected",
            "rejected");

        using var activity = await ownerClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations?limit=50",
            TestContext.Current.CancellationToken);
        var activityPage =
            await OrganizationEndpointTestSupport.ReadDataAsync(activity);
        Assert.Equal(2, activityPage.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, activityPage.GetProperty("nextCursor").ValueKind);

        OrganizationEndpointTestSupport.AssertNoStore(
            create,
            duplicate,
            mismatch,
            unverified,
            account,
            unverifiedAccept,
            accept,
            createRejected,
            reject,
            activity);
        var audits = logs.Logs.Where(log => log.Category == AuditCategory).ToArray();
        Assert.Equal(10, audits.Length);
        Assert.All(audits, audit => Assert.IsType<Guid>(audit.State["UserId"]));
        Assert.DoesNotContain(recipient.Email, RenderedLogs(audits));
        Assert.DoesNotContain(rejectedRecipient.Email, RenderedLogs(audits));
        Assert.DoesNotContain("member", RenderedLogs(audits));
        Assert.DoesNotContain("/invite/", RenderedLogs(audits));
    }

    [Fact]
    public async Task InvitationCreateEnforcesRoleDomainTeamAndDuplicateOutcomes()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Role Owner",
            "local-agent+role-owner@local-agent.test");
        using var adminClient = factory.CreateApiClient();
        var admin = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            adminClient,
            "Role Admin",
            "local-agent+role-admin@local-agent.test");
        using var memberClient = factory.CreateApiClient();
        var member = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            memberClient,
            "Role Member",
            "local-agent+role-member@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Role Invitation Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        foreach (var (userId, role) in new[]
                 {
                     (admin.UserId, "admin"),
                     (member.UserId, "member")
                 })
        {
            using var add = await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/members",
                new { userId, role });
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
        }

        using var memberDenied = await SendCreateAsync(
            memberClient,
            organizationId,
            "local-agent+member-denied@local-agent.test",
            "member");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            memberDenied,
            HttpStatusCode.Forbidden,
            "invitation_permission_denied");
        using var adminOwnerDenied = await SendCreateAsync(
            adminClient,
            organizationId,
            "local-agent+admin-owner-denied@local-agent.test",
            "owner");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            adminOwnerDenied,
            HttpStatusCode.Forbidden,
            "invitation_permission_denied");
        using var ownerRole = await SendCreateAsync(
            ownerClient,
            organizationId,
            "local-agent+owner-role@local-agent.test",
            "owner");
        Assert.Equal(HttpStatusCode.Created, ownerRole.StatusCode);
        using var alreadyMember = await SendCreateAsync(
            ownerClient,
            organizationId,
            member.Email,
            "member");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            alreadyMember,
            HttpStatusCode.Conflict,
            "invitation_recipient_already_member");
        using var restrictDomains =
            await OrganizationEndpointTestSupport.SendWithCsrfAsync(
                ownerClient,
                HttpMethod.Patch,
                $"/api/v1/organizations/{organizationId:D}",
                new { allowedEmailDomains = new[] { "local-agent.test" } });
        Assert.Equal(HttpStatusCode.OK, restrictDomains.StatusCode);
        using var domain = await SendCreateAsync(
            ownerClient,
            organizationId,
            "outside@example.test",
            "member");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            domain,
            HttpStatusCode.BadRequest,
            "invitation_domain_restricted");
        using var team = await SendCreateAsync(
            ownerClient,
            organizationId,
            "local-agent+invalid-team@local-agent.test",
            "member",
            Guid.NewGuid());
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            team,
            HttpStatusCode.BadRequest,
            "invitation_team_invalid");
        OrganizationEndpointTestSupport.AssertNoStore(
            memberDenied,
            adminOwnerDenied,
            ownerRole,
            alreadyMember,
            domain,
            team);
    }

    [Fact]
    public async Task InvitationBoundaryRequiresSessionCsrfStrictJsonAndCanonicalInputs()
    {
        var organizationId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        using var anonymous = factory.CreateApiClient();
        using var anonymousRead = await anonymous.GetAsync(
            $"/api/v1/invitations/{invitationId:D}",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            anonymousRead,
            HttpStatusCode.Unauthorized,
            "unauthorized");

        using var client = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict Invitation Owner",
            "local-agent+strict-invitation@local-agent.test");
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var csrf = await client.PostAsJsonAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations",
            new
            {
                email = "local-agent+csrf@local-agent.test",
                role = "member"
            },
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            csrf,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
        Assert.DoesNotContain(logs.Logs, log => log.Category == AuditCategory);

        using var unknown = await OrganizationEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/invitations",
            "{\"email\":\"local-agent+strict-body@local-agent.test\",\"role\":\"member\",\"token\":\"secret-input\"}",
            "application/json");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            unknown,
            HttpStatusCode.BadRequest,
            "invalid_request");
        Assert.DoesNotContain("secret-input", RenderedLogs(logs.Logs));

        using var invalidEmail = await SendCreateAsync(
            client,
            organizationId,
            "not-an-email",
            "member");
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidEmail,
            "email");
        using var invalidRole = await SendCreateAsync(
            client,
            organizationId,
            "local-agent+role@local-agent.test",
            "Owner");
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidRole,
            "role");
        using var invalidTeam = await OrganizationEndpointTestSupport
            .SendWithCsrfAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/organizations/{organizationId:D}/invitations",
                new
                {
                    email = "local-agent+team@local-agent.test",
                    role = "member",
                    teamId = "not-a-uuid"
                });
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidTeam,
            "teamId");
        using var invalidRoute = await client.GetAsync(
            "/api/v1/invitations/not-a-uuid",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidRoute,
            "invitationId");
        using var invalidStatus = await client.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations?status=Pending",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidStatus,
            "status");
        using var duplicateStatus = await client.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations?status=pending&status=rejected",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            duplicateStatus,
            "status");
        using var invalidLimit = await client.GetAsync(
            "/api/v1/account/invitations?limit=999999999999999999999999",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            invalidLimit,
            "limit");
        using var duplicateLimit = await client.GetAsync(
            "/api/v1/account/invitations?limit=1&limit=2",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertValidationProblemAsync(
            duplicateLimit,
            "limit");
        using var bodyOnDecision = await OrganizationEndpointTestSupport
            .SendRawWithCsrfAsync(
                client,
                HttpMethod.Post,
                $"/api/v1/invitations/{invitationId:D}/reject",
                "{}",
                "application/json");
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            bodyOnDecision,
            HttpStatusCode.BadRequest,
            "invalid_request");
        OrganizationEndpointTestSupport.AssertNoStore(
            anonymousRead,
            csrf,
            unknown,
            invalidEmail,
            invalidRole,
            invalidTeam,
            invalidRoute,
            invalidStatus,
            duplicateStatus,
            invalidLimit,
            duplicateLimit,
            bodyOnDecision);
    }

    [Fact]
    public async Task OrganizationInvitationActivityFilterAndCursorAreValidatedAndContinued()
    {
        using var ownerClient = factory.CreateApiClient();
        await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Paging Owner",
            "local-agent+paging-owner@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Paging Invitation Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        foreach (var suffix in new[] { "first", "second" })
        {
            using var created = await SendCreateAsync(
                ownerClient,
                organizationId,
                $"local-agent+paging-{suffix}@local-agent.test",
                "member");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var first = await ownerClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations?status=pending&limit=1",
            TestContext.Current.CancellationToken);
        var firstPage = await OrganizationEndpointTestSupport.ReadDataAsync(first);
        Assert.Single(firstPage.GetProperty("items").EnumerateArray());
        var cursor = firstPage.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        using var second = await ownerClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations?status=pending&limit=1&cursor={Uri.EscapeDataString(cursor!)}",
            TestContext.Current.CancellationToken);
        var secondPage = await OrganizationEndpointTestSupport.ReadDataAsync(second);
        Assert.Single(secondPage.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, secondPage.GetProperty("nextCursor").ValueKind);
        using var invalidCursor = await ownerClient.GetAsync(
            $"/api/v1/organizations/{organizationId:D}/invitations?cursor=invalid",
            TestContext.Current.CancellationToken);
        await OrganizationEndpointTestSupport.AssertProblemAsync(
            invalidCursor,
            HttpStatusCode.BadRequest,
            "invalid_cursor");
    }

    [Fact]
    public async Task InvitationCreateMapsTheOneHundredLivePendingActorCap()
    {
        using var ownerClient = factory.CreateApiClient();
        var owner = await OrganizationEndpointTestSupport.CreateScenarioAsync(
            ownerClient,
            "Capped Invitation Owner",
            "local-agent+capped-invitation-owner@local-agent.test");
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                ownerClient,
                "Capped Invitation Workspace");
        var organizationId =
            (await OrganizationEndpointTestSupport.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.Invitations.AddRange(Enumerable.Range(0, 100).Select(index =>
                new InvitationEntity
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Email = $"local-agent+capped-{index}@local-agent.test",
                    Role = "member",
                    Status = "pending",
                    InviterUserId = owner.UserId,
                    CreatedAt = now.AddSeconds(-index - 1),
                    UpdatedAt = now.AddSeconds(-index - 1),
                    ExpiresAt = now.AddHours(24)
                }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await SendCreateAsync(
            ownerClient,
            organizationId,
            "local-agent+capped-overflow@local-agent.test",
            "member");

        await OrganizationEndpointTestSupport.AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "invitation_limit_reached");
        OrganizationEndpointTestSupport.AssertNoStore(response);
    }

    private static Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        Guid organizationId,
        string email,
        string role,
        Guid? teamId = null) =>
        OrganizationEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/invitations",
            new { email, role, teamId });

    private static void AssertInvitation(
        JsonElement invitation,
        Guid invitationId,
        Guid organizationId,
        string email,
        string role,
        string status,
        string displayState)
    {
        Assert.Equal(invitationId, invitation.GetProperty("id").GetGuid());
        Assert.Equal(
            organizationId,
            invitation.GetProperty("organizationId").GetGuid());
        Assert.Equal(email, invitation.GetProperty("email").GetString());
        Assert.Equal(role, invitation.GetProperty("role").GetString());
        Assert.Equal(status, invitation.GetProperty("status").GetString());
        Assert.Equal(
            displayState,
            invitation.GetProperty("displayState").GetString());
        Assert.Equal(
            $"/invite/{invitationId:D}",
            invitation.GetProperty("invitationPath").GetString());
        Assert.NotEqual(
            default,
            invitation.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.NotEqual(
            default,
            invitation.GetProperty("createdAt").GetDateTimeOffset());
    }

    private static string RenderedLogs(IEnumerable<CapturedLog> logs) =>
        string.Join(
            Environment.NewLine,
            logs.Select(log => string.Join(
                " ",
                new[] { log.Message }
                    .Concat(log.State.Values.Select(value =>
                        value?.ToString() ?? string.Empty))
                    .Concat(log.Scope.Values.Select(value =>
                        value?.ToString() ?? string.Empty)))));
}
