using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Template.Api.Tests.Infrastructure;
using Template.Application.Authentication;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;
using Template.Infrastructure.Collaboration;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Collaboration;

public sealed class InvitationStoreTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Create_normalizes_recipient_and_persists_a_48_hour_uuid_v4_invitation()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync(
            "create-owner@allowed.test",
            "Invitation Owner");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Invitation Workspace");
        await fixture.SetAllowedDomainsAsync(organization, "allowed.test");
        var team = await fixture.CreateTeamAsync(organization, "Core Team");

        var result = await fixture.Store.CreateAsync(
            new CreateInvitationCommand(
                owner.UserId,
                organization,
                "  RECIPIENT@ALLOWED.TEST ",
                OrganizationRole.Admin,
                team),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Value!.Id.Value.Version);
        Assert.Equal("recipient@allowed.test", result.Value.Email);
        Assert.Equal(InvitationStoreFixture.Now, result.Value.CreatedAt);
        Assert.Equal(
            InvitationStoreFixture.Now.AddHours(48),
            result.Value.ExpiresAt);
        Assert.Equal("Invitation Workspace", result.Value.OrganizationName);
        Assert.Equal("Core Team", result.Value.TeamName);
        Assert.Equal("Invitation Owner", result.Value.InviterName);
        Assert.Equal(InvitationDisplayState.Pending, result.Value.DisplayState);
    }

    [Fact]
    public async Task Organization_list_filters_pending_and_expired_at_the_exact_boundary()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("list-owner@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Activity");
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            "pending@example.test",
            expiresAt: InvitationStoreFixture.Now.AddHours(1));

        var pending = await fixture.Store.ListOrganizationAsync(
            owner.UserId,
            organization,
            InvitationDisplayState.Pending,
            after: null,
            limit: 50,
            InvitationStoreFixture.Now.AddMinutes(59),
            TestContext.Current.CancellationToken);
        var expired = await fixture.Store.ListOrganizationAsync(
            owner.UserId,
            organization,
            InvitationDisplayState.Expired,
            after: null,
            limit: 50,
            InvitationStoreFixture.Now.AddHours(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(invitation, Assert.Single(pending.Value!.Items).Id);
        Assert.Equal(InvitationDisplayState.Pending, pending.Value.Items[0].DisplayState);
        Assert.Equal(invitation, Assert.Single(expired.Value!.Items).Id);
        Assert.Equal(InvitationDisplayState.Expired, expired.Value.Items[0].DisplayState);
    }

    [Fact]
    public async Task Organization_list_pages_by_created_at_descending_then_uuid_descending()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("org-page-owner@example.test");
        var foreignOwner = await fixture.CreateUserAsync("org-page-foreign@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Organization Paging");
        var foreignOrganization = await fixture.CreateOrganizationAsync(
            foreignOwner,
            OrganizationRole.Owner,
            "Foreign Organization Paging");
        var first = Invitation("10000000-0000-4000-8000-000000000005");
        var second = Invitation("10000000-0000-4000-8000-000000000004");
        var third = Invitation("10000000-0000-4000-8000-000000000003");
        var fourth = Invitation("10000000-0000-4000-8000-000000000008");
        var fifth = Invitation("10000000-0000-4000-8000-000000000001");
        var newest = InvitationStoreFixture.Now.AddMinutes(-1);
        var middle = InvitationStoreFixture.Now.AddMinutes(-2);
        await fixture.SeedInvitationAsync(
            organization,
            owner,
            "org-page-1@example.test",
            invitationId: first,
            createdAt: newest);
        await fixture.SeedInvitationAsync(
            organization,
            owner,
            "org-page-2@example.test",
            invitationId: second,
            createdAt: newest);
        await fixture.SeedInvitationAsync(
            organization,
            owner,
            "org-page-3@example.test",
            invitationId: third,
            createdAt: newest);
        await fixture.SeedInvitationAsync(
            organization,
            owner,
            "org-page-4@example.test",
            invitationId: fourth,
            createdAt: middle);
        await fixture.SeedInvitationAsync(
            organization,
            owner,
            "org-page-5@example.test",
            invitationId: fifth,
            createdAt: InvitationStoreFixture.Now.AddMinutes(-3));
        await fixture.SeedInvitationAsync(
            foreignOrganization,
            foreignOwner,
            "org-page-hidden@example.test",
            invitationId: Invitation("ffffffff-ffff-4fff-8fff-ffffffffffff"),
            createdAt: InvitationStoreFixture.Now);

        var pageOne = await fixture.Store.ListOrganizationAsync(
            owner.UserId,
            organization,
            filter: null,
            after: null,
            limit: 2,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);
        var afterOne = Assert.IsType<OrganizationInvitationCursorPosition>(
            pageOne.Value!.Next);
        var pageTwo = await fixture.Store.ListOrganizationAsync(
            owner.UserId,
            organization,
            filter: null,
            afterOne,
            limit: 2,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);
        var afterTwo = Assert.IsType<OrganizationInvitationCursorPosition>(
            pageTwo.Value!.Next);
        var pageThree = await fixture.Store.ListOrganizationAsync(
            owner.UserId,
            organization,
            filter: null,
            afterTwo,
            limit: 2,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);

        Assert.Equal([first, second], pageOne.Value.Items.Select(row => row.Id));
        Assert.Equal(newest, afterOne.CreatedAt);
        Assert.Equal(second, afterOne.Id);
        Assert.Equal([third, fourth], pageTwo.Value.Items.Select(row => row.Id));
        Assert.Equal(middle, afterTwo.CreatedAt);
        Assert.Equal(fourth, afterTwo.Id);
        Assert.Equal(fifth, Assert.Single(pageThree.Value!.Items).Id);
        Assert.Null(pageThree.Value.Next);
        Assert.Equal(
            new[] { first, second, third, fourth, fifth },
            pageOne.Value.Items
                .Concat(pageTwo.Value.Items)
                .Concat(pageThree.Value.Items)
                .Select(row => row.Id));
    }

    [Fact]
    public async Task Account_list_pages_by_expiry_ascending_then_created_and_uuid_descending()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var recipient = await fixture.CreateUserAsync("account-page-recipient@example.test");
        var owner = await fixture.CreateUserAsync("account-page-owner@example.test");
        var organizations = new List<OrganizationId>();
        for (var index = 0; index < 7; index++)
        {
            organizations.Add(await fixture.CreateOrganizationAsync(
                owner,
                OrganizationRole.Owner,
                $"Account Paging {index}"));
        }

        var first = Invitation("20000000-0000-4000-8000-000000000005");
        var second = Invitation("20000000-0000-4000-8000-000000000004");
        var third = Invitation("20000000-0000-4000-8000-000000000009");
        var fourth = Invitation("20000000-0000-4000-8000-000000000008");
        var fifth = Invitation("20000000-0000-4000-8000-000000000007");
        var earlyExpiry = InvitationStoreFixture.Now.AddHours(1);
        var laterExpiry = InvitationStoreFixture.Now.AddHours(2);
        var newest = InvitationStoreFixture.Now.AddMinutes(-1);
        await fixture.SeedInvitationAsync(
            organizations[0], owner, recipient.Email,
            invitationId: first, createdAt: newest, expiresAt: earlyExpiry);
        await fixture.SeedInvitationAsync(
            organizations[1], owner, recipient.Email,
            invitationId: second, createdAt: newest, expiresAt: earlyExpiry);
        await fixture.SeedInvitationAsync(
            organizations[2], owner, recipient.Email,
            invitationId: third,
            createdAt: InvitationStoreFixture.Now.AddMinutes(-2),
            expiresAt: earlyExpiry);
        await fixture.SeedInvitationAsync(
            organizations[3], owner, recipient.Email,
            invitationId: fourth, createdAt: newest, expiresAt: laterExpiry);
        await fixture.SeedInvitationAsync(
            organizations[4], owner, recipient.Email,
            invitationId: fifth, createdAt: newest, expiresAt: laterExpiry);
        await fixture.AddOrganizationMemberAsync(
            organizations[5],
            recipient,
            OrganizationRole.Member);
        await fixture.SeedInvitationAsync(
            organizations[5], owner, recipient.Email,
            invitationId: Invitation("ffffffff-ffff-4fff-8fff-fffffffffffe"),
            createdAt: InvitationStoreFixture.Now,
            expiresAt: earlyExpiry);
        await fixture.SeedInvitationAsync(
            organizations[6], owner, "other-recipient@example.test",
            invitationId: Invitation("ffffffff-ffff-4fff-8fff-ffffffffffff"),
            createdAt: InvitationStoreFixture.Now,
            expiresAt: earlyExpiry);

        var pageOne = await fixture.Store.ListAccountAsync(
            recipient.InvitationActor,
            after: null,
            limit: 2,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);
        var afterOne = Assert.IsType<AccountInvitationCursorPosition>(
            pageOne.Value!.Next);
        var pageTwo = await fixture.Store.ListAccountAsync(
            recipient.InvitationActor,
            afterOne,
            limit: 2,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);
        var afterTwo = Assert.IsType<AccountInvitationCursorPosition>(
            pageTwo.Value!.Next);
        var pageThree = await fixture.Store.ListAccountAsync(
            recipient.InvitationActor,
            afterTwo,
            limit: 2,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);

        Assert.Equal([first, second], pageOne.Value.Items.Select(row => row.Id));
        Assert.Equal(earlyExpiry, afterOne.ExpiresAt);
        Assert.Equal(newest, afterOne.CreatedAt);
        Assert.Equal(second, afterOne.Id);
        Assert.Equal([third, fourth], pageTwo.Value.Items.Select(row => row.Id));
        Assert.Equal(laterExpiry, afterTwo.ExpiresAt);
        Assert.Equal(newest, afterTwo.CreatedAt);
        Assert.Equal(fourth, afterTwo.Id);
        Assert.Equal(fifth, Assert.Single(pageThree.Value!.Items).Id);
        Assert.Null(pageThree.Value.Next);
        Assert.Equal(
            new[] { first, second, third, fourth, fifth },
            pageOne.Value.Items
                .Concat(pageTwo.Value.Items)
                .Concat(pageThree.Value.Items)
                .Select(row => row.Id));
    }

    [Fact]
    public async Task Member_cannot_create_or_read_organization_invitation_activity()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("permission-owner@example.test");
        var member = await fixture.CreateUserAsync("permission-member@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Permission");
        await fixture.AddOrganizationMemberAsync(
            organization,
            member,
            OrganizationRole.Member);

        var create = await fixture.Store.CreateAsync(
            new CreateInvitationCommand(
                member.UserId,
                organization,
                "recipient@example.test",
                OrganizationRole.Member,
                TeamId: null),
            TestContext.Current.CancellationToken);
        var list = await fixture.Store.ListOrganizationAsync(
            member.UserId,
            organization,
            filter: null,
            after: null,
            limit: 50,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.PermissionDenied, create.Failure);
        Assert.Equal(InvitationFailure.PermissionDenied, list.Failure);
    }

    [Fact]
    public async Task Create_rechecks_role_team_domain_and_existing_membership()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("validation-owner@allowed.test");
        var admin = await fixture.CreateUserAsync("validation-admin@allowed.test");
        var existing = await fixture.CreateUserAsync("existing@allowed.test");
        var foreignOwner = await fixture.CreateUserAsync("foreign@allowed.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Validation");
        await fixture.AddOrganizationMemberAsync(
            organization,
            admin,
            OrganizationRole.Admin);
        await fixture.AddOrganizationMemberAsync(
            organization,
            existing,
            OrganizationRole.Member);
        await fixture.SetAllowedDomainsAsync(organization, "allowed.test");
        var foreignOrganization = await fixture.CreateOrganizationAsync(
            foreignOwner,
            OrganizationRole.Owner,
            "Foreign");
        var foreignTeam = await fixture.CreateTeamAsync(
            foreignOrganization,
            "Foreign Team");

        var ownerRole = await fixture.Store.CreateAsync(
            new(admin.UserId, organization, "role@allowed.test", OrganizationRole.Owner, null),
            TestContext.Current.CancellationToken);
        var team = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, "team@allowed.test", OrganizationRole.Member, foreignTeam),
            TestContext.Current.CancellationToken);
        var domain = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, "outside@other.test", OrganizationRole.Member, null),
            TestContext.Current.CancellationToken);
        var member = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, existing.Email, OrganizationRole.Member, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.PermissionDenied, ownerRole.Failure);
        Assert.Equal(InvitationFailure.TeamInvalid, team.Failure);
        Assert.Equal(InvitationFailure.DomainRestricted, domain.Failure);
        Assert.Equal(InvitationFailure.RecipientAlreadyMember, member.Failure);
    }

    [Fact]
    public async Task Live_duplicate_is_rejected_but_expired_duplicate_is_canceled_and_replaced()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("duplicate-owner@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Duplicates");
        var expired = await fixture.SeedInvitationAsync(
            organization,
            owner,
            "expired@example.test",
            expiresAt: InvitationStoreFixture.Now);
        await fixture.SeedInvitationAsync(
            organization,
            owner,
            "live@example.test",
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var replacement = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, "EXPIRED@example.test", OrganizationRole.Member, null),
            TestContext.Current.CancellationToken);
        var duplicate = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, "live@example.test", OrganizationRole.Member, null),
            TestContext.Current.CancellationToken);

        Assert.True(replacement.Succeeded);
        Assert.NotEqual(expired, replacement.Value!.Id);
        Assert.Equal(InvitationFailure.AlreadyExists, duplicate.Failure);
        Assert.Equal(
            InvitationStatus.Canceled.Value,
            await fixture.InvitationStatusAsync(expired));
        Assert.Equal(2, await fixture.CountPendingInvitationsAsync(organization));
    }

    [Fact]
    public async Task Create_enforces_one_hundred_live_pending_invitations_per_actor_and_organization()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("cap-owner@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Cap");
        for (var index = 0; index < 100; index++)
        {
            await fixture.SeedInvitationAsync(
                organization,
                owner,
                $"cap-{index:D3}@example.test",
                expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        }

        var result = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, "one-too-many@example.test", OrganizationRole.Member, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.LimitReached, result.Failure);
        Assert.Equal(100, await fixture.CountPendingInvitationsAsync(organization));
    }

    [Fact]
    public async Task Account_list_returns_only_live_matching_inaccessible_invitations()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var recipient = await fixture.CreateUserAsync("recipient@example.test");
        var firstOwner = await fixture.CreateUserAsync("account-owner-1@example.test");
        var secondOwner = await fixture.CreateUserAsync("account-owner-2@example.test");
        var thirdOwner = await fixture.CreateUserAsync("account-owner-3@example.test");
        var actionableOrganization = await fixture.CreateOrganizationAsync(
            firstOwner,
            OrganizationRole.Owner,
            "Actionable");
        var accessibleOrganization = await fixture.CreateOrganizationAsync(
            secondOwner,
            OrganizationRole.Owner,
            "Accessible");
        await fixture.AddOrganizationMemberAsync(
            accessibleOrganization,
            recipient,
            OrganizationRole.Member);
        var expiredOrganization = await fixture.CreateOrganizationAsync(
            thirdOwner,
            OrganizationRole.Owner,
            "Expired");
        var expected = await fixture.SeedInvitationAsync(
            actionableOrganization,
            firstOwner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        await fixture.SeedInvitationAsync(
            accessibleOrganization,
            secondOwner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        await fixture.SeedInvitationAsync(
            expiredOrganization,
            thirdOwner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now);
        await fixture.SeedInvitationAsync(
            expiredOrganization,
            thirdOwner,
            "someone-else@example.test",
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var result = await fixture.Store.ListAccountAsync(
            recipient.InvitationActor,
            after: null,
            limit: 50,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, Assert.Single(result.Value!.Items).Id);
        Assert.Equal("Actionable", result.Value.Items[0].OrganizationName);
    }

    [Fact]
    public async Task Decision_recipient_mismatch_returns_no_invitation_projection()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("mismatch-owner@example.test");
        var recipient = await fixture.CreateUserAsync("mismatch-recipient@example.test");
        var stranger = await fixture.CreateUserAsync("mismatch-stranger@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Mismatch");
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var result = await fixture.Store.GetDecisionAsync(
            stranger.InvitationActor,
            invitation,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.RecipientMismatch, result.Failure);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Decision_classifies_unverified_domain_restricted_and_already_member_states()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("decision-owner@allowed.test");
        var unverified = await fixture.CreateUserAsync(
            "unverified@allowed.test",
            emailVerified: false);
        var restricted = await fixture.CreateUserAsync("restricted@other.test");
        var member = await fixture.CreateUserAsync("member@allowed.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Decision");
        await fixture.SetAllowedDomainsAsync(organization, "allowed.test");
        await fixture.AddOrganizationMemberAsync(
            organization,
            member,
            OrganizationRole.Member);
        var unverifiedInvitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            unverified.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        var restrictedInvitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            restricted.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        var memberInvitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            member.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var unverifiedResult = await fixture.Store.GetDecisionAsync(
            unverified.InvitationActor,
            unverifiedInvitation,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);
        var restrictedResult = await fixture.Store.GetDecisionAsync(
            restricted.InvitationActor,
            restrictedInvitation,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);
        var memberResult = await fixture.Store.GetDecisionAsync(
            member.InvitationActor,
            memberInvitation,
            InvitationStoreFixture.Now,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            InvitationDecisionState.EmailVerificationRequired,
            unverifiedResult.Value!.State);
        Assert.False(unverifiedResult.Value.CanRespond);
        Assert.Equal(
            InvitationDecisionState.DomainRestricted,
            restrictedResult.Value!.State);
        Assert.False(restrictedResult.Value.CanRespond);
        Assert.Equal(
            InvitationDecisionState.AlreadyMember,
            memberResult.Value!.State);
        Assert.False(memberResult.Value.CanRespond);
    }

    [Fact]
    public async Task Accept_creates_both_memberships_sets_active_and_marks_accepted()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("accept-owner@example.test");
        var recipient = await fixture.CreateUserAsync("accept-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Acceptance");
        var team = await fixture.CreateTeamAsync(organization, "Accept Team");
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            team,
            OrganizationRole.Admin,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var result = await fixture.Store.AcceptAsync(
            new AcceptInvitationCommand(
                recipient.InvitationActor,
                session,
                invitation),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(organization, result.Value!.OrganizationId);
        Assert.Equal("acceptance", result.Value.CanonicalOrganizationKey);
        await using var db = fixture.CreateDbContext();
        var membership = await db.OrganizationMembers.SingleAsync(
            row => row.OrganizationId == organization.Value &&
                   row.UserId == recipient.UserId.Value,
            TestContext.Current.CancellationToken);
        Assert.Equal(OrganizationRole.Admin.Value, membership.Role);
        Assert.Single(await db.TeamMembers.Where(
                row => row.TeamId == team.Value &&
                       row.OrganizationMemberId == membership.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            InvitationStatus.Accepted.Value,
            (await db.Invitations.SingleAsync(
                row => row.Id == invitation.Value,
                TestContext.Current.CancellationToken)).Status);
        Assert.Equal(
            organization.Value,
            (await db.Sessions.SingleAsync(
                row => row.Id == session.Value,
                TestContext.Current.CancellationToken)).ActiveOrganizationId);
    }

    [Fact]
    public async Task Accept_locks_current_session_before_organization_name_advisory_key()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("lock-order-owner@example.test");
        var recipient = await fixture.CreateUserAsync("lock-order-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Lock Order");
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        fixture.ResetCommandOrder();

        var result = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, session, invitation),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var commands = fixture.CommandOrder;
        var sessionLock = Assert.Single(
            commands.Select((sql, index) => (sql, index)),
            value =>
                value.sql.Contains("FROM auth.sessions", StringComparison.Ordinal) &&
                value.sql.Contains("FOR UPDATE", StringComparison.Ordinal));
        var advisoryLock = Assert.Single(
            commands.Select((sql, index) => (sql, index)),
            value => value.sql.Contains(
                "pg_advisory_xact_lock",
                StringComparison.Ordinal));
        Assert.True(
            sessionLock.index < advisoryLock.index,
            $"Expected session lock before advisory lock, got indexes {sessionLock.index} and {advisoryLock.index}.");
    }

    [Fact]
    public async Task Accept_rejects_accessible_organization_name_conflict_without_partial_writes()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("conflict-owner@example.test");
        var otherOwner = await fixture.CreateUserAsync("conflict-other-owner@example.test");
        var recipient = await fixture.CreateUserAsync("conflict-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Same Name");
        var accessible = await fixture.CreateOrganizationAsync(
            otherOwner,
            OrganizationRole.Owner,
            "same name");
        await fixture.AddOrganizationMemberAsync(
            accessible,
            recipient,
            OrganizationRole.Member);
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var result = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, session, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.MembershipConflict, result.Failure);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
        Assert.False(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
        Assert.Null(await fixture.ActiveOrganizationAsync(session));
    }

    [Fact]
    public async Task Accept_at_the_exact_expiry_boundary_is_expired_without_partial_writes()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("expiry-owner@example.test");
        var recipient = await fixture.CreateUserAsync("expiry-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Expiry");
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now);

        var result = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, session, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.Expired, result.Failure);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
        Assert.False(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
        Assert.Null(await fixture.ActiveOrganizationAsync(session));
    }

    [Fact]
    public async Task Mutations_recheck_recipient_verification_domain_and_pending_status()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("recheck-owner@allowed.test");
        var recipient = await fixture.CreateUserAsync("recheck-recipient@allowed.test");
        var unverified = await fixture.CreateUserAsync(
            "recheck-unverified@allowed.test",
            emailVerified: false);
        var stranger = await fixture.CreateUserAsync("recheck-stranger@allowed.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Rechecks");
        await fixture.SetAllowedDomainsAsync(organization, "other.test");
        var recipientSession = await fixture.CreateSessionAsync(recipient);
        var strangerSession = await fixture.CreateSessionAsync(stranger);
        var restrictedInvitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        var unverifiedInvitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            unverified.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        var terminalInvitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            status: InvitationStatus.Accepted,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var mismatch = await fixture.Store.AcceptAsync(
            new(stranger.InvitationActor, strangerSession, restrictedInvitation),
            TestContext.Current.CancellationToken);
        var verification = await fixture.Store.RejectAsync(
            new(unverified.InvitationActor, unverifiedInvitation),
            TestContext.Current.CancellationToken);
        var domain = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, recipientSession, restrictedInvitation),
            TestContext.Current.CancellationToken);
        var status = await fixture.Store.RejectAsync(
            new(recipient.InvitationActor, terminalInvitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.RecipientMismatch, mismatch.Failure);
        Assert.Equal(
            InvitationFailure.EmailVerificationRequired,
            verification.Failure);
        Assert.Equal(InvitationFailure.DomainRestricted, domain.Failure);
        Assert.Equal(InvitationFailure.NotPending, status.Failure);
        Assert.False(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
    }

    [Fact]
    public async Task Reject_changes_only_the_matching_pending_invitation_status()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("reject-owner@example.test");
        var recipient = await fixture.CreateUserAsync("reject-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Rejection");
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));

        var result = await fixture.Store.RejectAsync(
            new RejectInvitationCommand(recipient.InvitationActor, invitation),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(InvitationDecisionState.Rejected, result.Value!.State);
        Assert.False(result.Value.CanRespond);
        Assert.Equal(
            InvitationStatus.Rejected.Value,
            await fixture.InvitationStatusAsync(invitation));
        Assert.False(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
    }

    [Fact]
    public async Task Create_gets_a_full_48_hours_from_the_clock_after_waiting_for_the_lock()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("clock-create-owner@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Clock Create");
        var acquiredAt = InvitationStoreFixture.Now.AddHours(6);
        fixture.AdvanceClockOnNextPendingDuplicateLock(acquiredAt);

        var result = await fixture.Store.CreateAsync(
            new(
                owner.UserId,
                organization,
                "clock-create-recipient@example.test",
                OrganizationRole.Member,
                TeamId: null),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(acquiredAt, result.Value!.CreatedAt);
        Assert.Equal(acquiredAt.AddHours(48), result.Value.ExpiresAt);
    }

    [Fact]
    public async Task Accept_rechecks_the_expiry_boundary_after_waiting_for_locks()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("clock-accept-owner@example.test");
        var recipient = await fixture.CreateUserAsync(
            "clock-accept-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Clock Accept");
        var session = await fixture.CreateSessionAsync(recipient);
        var expiresAt = InvitationStoreFixture.Now.AddMinutes(1);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: expiresAt);
        fixture.AdvanceClockOnNextOrganizationLock(expiresAt);

        var result = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, session, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.Expired, result.Failure);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
        Assert.False(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
    }

    [Fact]
    public async Task Reject_rechecks_the_expiry_boundary_after_waiting_for_locks()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("clock-reject-owner@example.test");
        var recipient = await fixture.CreateUserAsync(
            "clock-reject-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Clock Reject");
        var expiresAt = InvitationStoreFixture.Now.AddMinutes(1);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: expiresAt);
        fixture.AdvanceClockOnNextOrganizationLock(expiresAt);

        var result = await fixture.Store.RejectAsync(
            new(recipient.InvitationActor, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.Expired, result.Failure);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
    }

    [Fact]
    public async Task Accept_rechecks_session_expiry_after_waiting_for_locks()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("clock-session-owner@example.test");
        var recipient = await fixture.CreateUserAsync(
            "clock-session-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Clock Session");
        var sessionExpiresAt = InvitationStoreFixture.Now.AddMinutes(1);
        var session = await fixture.CreateSessionAsync(
            recipient,
            sessionExpiresAt);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddHours(1));
        fixture.AdvanceClockOnNextOrganizationLock(sessionExpiresAt);

        var result = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, session, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.NotFound, result.Failure);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
        Assert.False(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
    }

    [Fact]
    public async Task Accept_gets_fresh_time_on_a_serialization_retry()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("clock-retry-owner@example.test");
        var recipient = await fixture.CreateUserAsync(
            "clock-retry-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Clock Retry");
        var session = await fixture.CreateSessionAsync(recipient);
        var expiresAt = InvitationStoreFixture.Now.AddMinutes(1);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: expiresAt);
        fixture.FailNextAllowedDomainReadWithSerialization(expiresAt);

        var result = await fixture.Store.AcceptAsync(
            new(recipient.InvitationActor, session, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.Expired, result.Failure);
        Assert.Equal(2, fixture.OrganizationLockAttempts);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
    }

    [Fact]
    public async Task Reject_does_not_change_a_pending_invitation_for_an_existing_member()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("member-reject-owner@example.test");
        var recipient = await fixture.CreateUserAsync(
            "member-reject-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Member Reject");
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        await fixture.AddOrganizationMemberAsync(
            organization,
            recipient,
            OrganizationRole.Member);

        var result = await fixture.Store.RejectAsync(
            new(recipient.InvitationActor, invitation),
            TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.RecipientAlreadyMember, result.Failure);
        Assert.Equal(
            InvitationStatus.Pending.Value,
            await fixture.InvitationStatusAsync(invitation));
    }

    private static InvitationId Invitation(string value) =>
        new(Guid.Parse(value));
}

internal sealed class InvitationStoreFixture : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainerFixture _postgres;
    private readonly string _databaseName;
    private readonly string _connectionString;
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _storeScope;

    private InvitationStoreFixture(
        PostgreSqlContainerFixture postgres,
        string databaseName,
        string connectionString,
        ServiceProvider services)
    {
        _postgres = postgres;
        _databaseName = databaseName;
        _connectionString = connectionString;
        _services = services;
        _storeScope = services.CreateAsyncScope();
    }

    internal IInvitationStore Store =>
        _storeScope.ServiceProvider.GetRequiredService<IInvitationStore>();

    internal static async Task<InvitationStoreFixture> CreateAsync(
        PostgreSqlContainerFixture postgres)
    {
        var database = await postgres.CreateDatabaseAsync(
            TestContext.Current.CancellationToken);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = database.ConnectionString,
                ["DataProtection:ApplicationName"] = "Template"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new InvitationTimeProvider(Now));
        services.AddSingleton<TimeProvider>(provider =>
            provider.GetRequiredService<InvitationTimeProvider>());
        services.AddSingleton<InvitationMutationStartBarrier>();
        services.AddSingleton<InvitationCommandOrderRecorder>();
        services.AddSingleton<InvitationClockTestInterceptor>();
        services.AddDbContext<TemplateDbContext>((provider, options) =>
            options.AddInterceptors(
                provider.GetRequiredService<InvitationMutationStartBarrier>(),
                provider.GetRequiredService<InvitationCommandOrderRecorder>(),
                provider.GetRequiredService<InvitationClockTestInterceptor>()));
        services.AddAuthInfrastructure(configuration, new TestHostEnvironment());
        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return new InvitationStoreFixture(
            postgres,
            database.DatabaseName,
            database.ConnectionString,
            provider);
    }

    internal TemplateDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>();
        TemplateDbContext.Configure(options, _connectionString);
        return new TemplateDbContext(options.Options);
    }

    internal void CoordinateNextMutationPair() =>
        _services.GetRequiredService<InvitationMutationStartBarrier>()
            .CoordinateNextPair();

    internal bool MutationPairWasCoordinated =>
        _services.GetRequiredService<InvitationMutationStartBarrier>()
            .WasCoordinated;

    internal void ResetCommandOrder() =>
        _services.GetRequiredService<InvitationCommandOrderRecorder>().Reset();

    internal IReadOnlyList<string> CommandOrder =>
        _services.GetRequiredService<InvitationCommandOrderRecorder>().Commands;

    internal void AdvanceClockOnNextOrganizationLock(DateTimeOffset now) =>
        _services.GetRequiredService<InvitationClockTestInterceptor>()
            .AdvanceClockOnNextOrganizationLock(now);

    internal void AdvanceClockOnNextPendingDuplicateLock(DateTimeOffset now) =>
        _services.GetRequiredService<InvitationClockTestInterceptor>()
            .AdvanceClockOnNextPendingDuplicateLock(now);

    internal void FailNextAllowedDomainReadWithSerialization(
        DateTimeOffset now) =>
        _services.GetRequiredService<InvitationClockTestInterceptor>()
            .FailNextAllowedDomainReadWithSerialization(now);

    internal int OrganizationLockAttempts =>
        _services.GetRequiredService<InvitationClockTestInterceptor>()
            .OrganizationLockAttempts;

    internal async Task<InvitationOperationResult<InvitationView>>
        CreateInvitationAsync(
            InvitationTestActor actor,
            OrganizationId organizationId,
            string email)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvitationStore>()
            .CreateAsync(
                new(
                    actor.UserId,
                    organizationId,
                    email,
                    OrganizationRole.Member,
                    TeamId: null),
                TestContext.Current.CancellationToken);
    }

    internal async Task<InvitationOperationResult<AcceptedInvitation>>
        AcceptInvitationAsync(
            InvitationTestActor actor,
            SessionId sessionId,
            InvitationId invitationId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvitationStore>()
            .AcceptAsync(
                new(actor.InvitationActor, sessionId, invitationId),
                TestContext.Current.CancellationToken);
    }

    internal async Task<InvitationOperationResult<InvitationDecision>>
        RejectInvitationAsync(
            InvitationTestActor actor,
            InvitationId invitationId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvitationStore>()
            .RejectAsync(
                new(actor.InvitationActor, invitationId),
                TestContext.Current.CancellationToken);
    }

    internal async Task<TeamOperationResult<TeamDeletion>> DeleteTeamAsync(
        InvitationTestActor actor,
        OrganizationId organizationId,
        TeamId teamId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ITeamStore>()
            .DeleteAsync(
                new(actor.UserId, organizationId, teamId),
                TestContext.Current.CancellationToken);
    }

    internal async Task<InvitationTestActor> CreateUserAsync(
        string email,
        string? displayName = null,
        bool emailVerified = true)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var userId = Guid.CreateVersion7(Now);
        await using var db = CreateDbContext();
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            EmailConfirmed = emailVerified,
            DisplayName = displayName ?? email.Split('@')[0],
            IsLocalAutomation = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            CreatedAt = Now,
            UpdatedAt = Now
        });
        db.UserEmails.Add(new UserEmailEntity
        {
            Id = Guid.CreateVersion7(Now),
            UserId = userId,
            Email = email,
            NormalizedEmail = normalized,
            IsPrimary = true,
            CreatedAt = Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new InvitationTestActor(
            new UserId(userId),
            email,
            normalized,
            emailVerified);
    }

    internal async Task<OrganizationId> CreateOrganizationAsync(
        InvitationTestActor actor,
        OrganizationRole role,
        string name)
    {
        var organizationId = OrganizationId.New();
        await using var db = CreateDbContext();
        var slugBase = name.Trim().ToLowerInvariant().Replace(' ', '-');
        var slug = await db.Organizations.AnyAsync(
            row => row.Slug == slugBase,
            TestContext.Current.CancellationToken)
            ? $"{slugBase}-{Guid.NewGuid():N}"
            : slugBase;
        db.Organizations.Add(new OrganizationEntity
        {
            Id = organizationId.Value,
            Name = name,
            Slug = slug,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        db.OrganizationMembers.Add(new OrganizationMemberEntity
        {
            Id = OrganizationMemberId.New().Value,
            OrganizationId = organizationId.Value,
            UserId = actor.UserId.Value,
            Role = role.Value,
            JoinedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return organizationId;
    }

    internal async Task<OrganizationMemberId> AddOrganizationMemberAsync(
        OrganizationId organizationId,
        InvitationTestActor actor,
        OrganizationRole role)
    {
        var id = OrganizationMemberId.New();
        await using var db = CreateDbContext();
        db.OrganizationMembers.Add(new OrganizationMemberEntity
        {
            Id = id.Value,
            OrganizationId = organizationId.Value,
            UserId = actor.UserId.Value,
            Role = role.Value,
            JoinedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task SetAllowedDomainsAsync(
        OrganizationId organizationId,
        params string[] domains)
    {
        await using var db = CreateDbContext();
        db.OrganizationAllowedEmailDomains.AddRange(domains.Select(domain =>
            new OrganizationAllowedEmailDomainEntity
            {
                OrganizationId = organizationId.Value,
                Domain = domain
            }));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<TeamId> CreateTeamAsync(
        OrganizationId organizationId,
        string name)
    {
        var id = TeamId.New(Now);
        await using var db = CreateDbContext();
        db.Teams.Add(new TeamEntity
        {
            Id = id.Value,
            OrganizationId = organizationId.Value,
            Name = name,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<SessionId> CreateSessionAsync(
        InvitationTestActor actor,
        DateTimeOffset? expiresAt = null)
    {
        var id = SessionId.New();
        await using var db = CreateDbContext();
        db.Sessions.Add(new AuthSessionEntity
        {
            Id = id.Value,
            UserId = actor.UserId.Value,
            ActiveOrganizationId = null,
            TicketKeyHash = Guid.NewGuid().ToByteArray(),
            ProtectedTicket = [1, 2, 3],
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = expiresAt ?? Now.AddDays(1),
            AuthenticationMethod = BrowserAuthenticationMethods.Local
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<InvitationId> SeedInvitationAsync(
        OrganizationId organizationId,
        InvitationTestActor inviter,
        string email,
        TeamId? teamId = null,
        OrganizationRole? role = null,
        InvitationStatus? status = null,
        InvitationId? invitationId = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null)
    {
        var id = invitationId ?? InvitationId.New();
        var created = createdAt ?? Now.AddHours(-1);
        await using var db = CreateDbContext();
        db.Invitations.Add(new InvitationEntity
        {
            Id = id.Value,
            OrganizationId = organizationId.Value,
            TeamId = teamId?.Value,
            Email = email.Trim().ToLowerInvariant(),
            Role = (role ?? OrganizationRole.Member).Value,
            Status = (status ?? InvitationStatus.Pending).Value,
            InviterUserId = inviter.UserId.Value,
            CreatedAt = created,
            UpdatedAt = created,
            ExpiresAt = expiresAt ?? Now.AddHours(47)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    internal async Task<string> InvitationStatusAsync(InvitationId invitationId)
    {
        await using var db = CreateDbContext();
        return await db.Invitations
            .Where(row => row.Id == invitationId.Value)
            .Select(row => row.Status)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountPendingInvitationsAsync(
        OrganizationId organizationId)
    {
        await using var db = CreateDbContext();
        return await db.Invitations.CountAsync(
            row => row.OrganizationId == organizationId.Value &&
                   row.Status == InvitationStatus.Pending.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<bool> HasOrganizationMembershipAsync(
        OrganizationId organizationId,
        InvitationTestActor actor)
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.AnyAsync(
            row => row.OrganizationId == organizationId.Value &&
                   row.UserId == actor.UserId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<int> CountOrganizationMembershipsAsync(
        OrganizationId organizationId,
        InvitationTestActor actor)
    {
        await using var db = CreateDbContext();
        return await db.OrganizationMembers.CountAsync(
            row => row.OrganizationId == organizationId.Value &&
                   row.UserId == actor.UserId.Value,
            TestContext.Current.CancellationToken);
    }

    internal async Task<Guid?> ActiveOrganizationAsync(SessionId sessionId)
    {
        await using var db = CreateDbContext();
        return await db.Sessions
            .Where(row => row.Id == sessionId.Value)
            .Select(row => row.ActiveOrganizationId)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _storeScope.DisposeAsync();
        await _services.DisposeAsync();
        await _postgres.DropDatabaseAsync(
            _databaseName,
            CancellationToken.None);
    }
}

internal sealed record InvitationTestActor(
    UserId UserId,
    string Email,
    string NormalizedEmail,
    bool EmailVerified)
{
    internal InvitationActor InvitationActor =>
        new(UserId, NormalizedEmail, EmailVerified);
}

internal sealed class InvitationTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    internal void SetUtcNow(DateTimeOffset value) => _now = value;
}

internal sealed class InvitationClockTestInterceptor(
    InvitationTimeProvider timeProvider) : DbCommandInterceptor
{
    private DateTimeOffset? _organizationLockTime;
    private DateTimeOffset? _pendingDuplicateLockTime;
    private DateTimeOffset? _serializationFailureTime;
    private int _organizationLockAttempts;

    internal int OrganizationLockAttempts =>
        Volatile.Read(ref _organizationLockAttempts);

    internal void AdvanceClockOnNextOrganizationLock(DateTimeOffset now)
    {
        _organizationLockTime = now;
        Interlocked.Exchange(ref _organizationLockAttempts, 0);
    }

    internal void AdvanceClockOnNextPendingDuplicateLock(DateTimeOffset now) =>
        _pendingDuplicateLockTime = now;

    internal void FailNextAllowedDomainReadWithSerialization(
        DateTimeOffset now)
    {
        _serializationFailureTime = now;
        Interlocked.Exchange(ref _organizationLockAttempts, 0);
    }

    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>>
        ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
    {
        if (IsOrganizationLock(command.CommandText))
        {
            Interlocked.Increment(ref _organizationLockAttempts);
            if (_organizationLockTime is { } lockTime)
            {
                _organizationLockTime = null;
                timeProvider.SetUtcNow(lockTime);
            }
        }

        if (_pendingDuplicateLockTime is { } duplicateLockTime &&
            command.CommandText.Contains(
                "FROM organizations.invitations",
                StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains(
                "status = 'pending'",
                StringComparison.OrdinalIgnoreCase) &&
            command.CommandText.Contains(
                "FOR UPDATE",
                StringComparison.OrdinalIgnoreCase))
        {
            _pendingDuplicateLockTime = null;
            timeProvider.SetUtcNow(duplicateLockTime);
        }

        if (_serializationFailureTime is { } failureTime &&
            command.CommandText.Contains(
                "organizations.allowed_email_domains",
                StringComparison.OrdinalIgnoreCase))
        {
            _serializationFailureTime = null;
            timeProvider.SetUtcNow(failureTime);
            throw new PostgresException(
                "deterministic serialization failure",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.SerializationFailure);
        }

        return ValueTask.FromResult(result);
    }

    private static bool IsOrganizationLock(string commandText) =>
        commandText.Contains(
            "FROM organizations.organizations",
            StringComparison.OrdinalIgnoreCase) &&
        commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
}

internal sealed class InvitationMutationStartBarrier : DbCommandInterceptor
{
    private int _enabled;
    private int _arrivals;
    private TaskCompletionSource _release = NewSignal();

    internal bool WasCoordinated => Volatile.Read(ref _arrivals) == 2;

    internal void CoordinateNextPair()
    {
        _arrivals = 0;
        _release = NewSignal();
        Volatile.Write(ref _enabled, 1);
    }

    public override async ValueTask<InterceptionResult<System.Data.Common.DbDataReader>>
        ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _enabled) == 0 ||
            !command.CommandText.Contains(
                "FROM organizations.organizations",
                StringComparison.OrdinalIgnoreCase) ||
            !command.CommandText.Contains(
                "FOR UPDATE",
                StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        if (Interlocked.Increment(ref _arrivals) == 2)
        {
            Volatile.Write(ref _enabled, 0);
            _release.TrySetResult();
        }

        await _release.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);
        return result;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class InvitationCommandOrderRecorder : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commands = new();

    internal IReadOnlyList<string> Commands => _commands.ToArray();

    internal void Reset()
    {
        while (_commands.TryDequeue(out _))
        {
        }
    }

    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>>
        ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
