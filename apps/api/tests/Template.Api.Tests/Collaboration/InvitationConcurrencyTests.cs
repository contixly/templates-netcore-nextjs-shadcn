using Microsoft.EntityFrameworkCore;
using Template.Api.Tests.Infrastructure;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Tests.Collaboration;

public sealed class InvitationConcurrencyTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Concurrent_duplicate_creates_have_one_success_and_one_classified_loser()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("race-owner@example.test");
        var admin = await fixture.CreateUserAsync("race-admin@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Duplicate Race");
        await fixture.AddOrganizationMemberAsync(
            organization,
            admin,
            OrganizationRole.Admin);
        fixture.CoordinateNextMutationPair();

        var attempts = await Task.WhenAll(
            fixture.CreateInvitationAsync(
                owner,
                organization,
                "same@example.test"),
            fixture.CreateInvitationAsync(
                admin,
                organization,
                "SAME@example.test"));

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == InvitationFailure.AlreadyExists));
        Assert.Equal(1, await fixture.CountPendingInvitationsAsync(organization));
    }

    [Fact]
    public async Task Concurrent_cap_creates_cannot_insert_more_than_one_hundred()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("cap-race-owner@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Cap Race");
        for (var index = 0; index < 99; index++)
        {
            await fixture.SeedInvitationAsync(
                organization,
                owner,
                $"race-cap-{index:D3}@example.test",
                expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        }

        fixture.CoordinateNextMutationPair();
        var attempts = await Task.WhenAll(
            fixture.CreateInvitationAsync(
                owner,
                organization,
                "cap-final-a@example.test"),
            fixture.CreateInvitationAsync(
                owner,
                organization,
                "cap-final-b@example.test"));

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == InvitationFailure.LimitReached));
        Assert.Equal(100, await fixture.CountPendingInvitationsAsync(organization));
    }

    [Fact]
    public async Task Concurrent_expired_reinvites_cancel_once_and_create_one_live_replacement()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("reinvite-race-owner@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Reinvite Race");
        var expired = await fixture.SeedInvitationAsync(
            organization,
            owner,
            "reinvite@example.test",
            expiresAt: InvitationStoreFixture.Now);
        fixture.CoordinateNextMutationPair();

        var attempts = await Task.WhenAll(
            fixture.CreateInvitationAsync(
                owner,
                organization,
                "reinvite@example.test"),
            fixture.CreateInvitationAsync(
                owner,
                organization,
                "REINVITE@example.test"));

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == InvitationFailure.AlreadyExists));
        Assert.Equal(
            InvitationStatus.Canceled.Value,
            await fixture.InvitationStatusAsync(expired));
        Assert.Equal(1, await fixture.CountPendingInvitationsAsync(organization));
    }

    [Fact]
    public async Task Concurrent_accepts_have_one_winner_and_one_not_pending_loser()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("accept-race-owner@example.test");
        var recipient = await fixture.CreateUserAsync("accept-race-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Accept Race");
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        fixture.CoordinateNextMutationPair();

        var attempts = await Task.WhenAll(
            fixture.AcceptInvitationAsync(recipient, session, invitation),
            fixture.AcceptInvitationAsync(recipient, session, invitation));

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == InvitationFailure.NotPending));
        Assert.True(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
        Assert.Equal(1, await fixture.CountOrganizationMembershipsAsync(
            organization,
            recipient));
    }

    [Fact]
    public async Task Concurrent_accept_and_reject_have_one_winner_and_one_not_pending_loser()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("decision-race-owner@example.test");
        var recipient = await fixture.CreateUserAsync("decision-race-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Decision Race");
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        fixture.CoordinateNextMutationPair();

        var acceptTask = fixture.AcceptInvitationAsync(
            recipient,
            session,
            invitation);
        var rejectTask = fixture.RejectInvitationAsync(recipient, invitation);
        await Task.WhenAll(acceptTask, rejectTask);
        var accept = await acceptTask;
        var reject = await rejectTask;

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.NotEqual(accept.Succeeded, reject.Succeeded);
        Assert.Equal(
            InvitationFailure.NotPending,
            accept.Succeeded ? reject.Failure : accept.Failure);
        var finalStatus = await fixture.InvitationStatusAsync(invitation);
        Assert.Contains(
            finalStatus,
            new[]
            {
                InvitationStatus.Accepted.Value,
                InvitationStatus.Rejected.Value
            });
        Assert.Equal(
            finalStatus == InvitationStatus.Accepted.Value,
            await fixture.HasOrganizationMembershipAsync(
                organization,
                recipient));
    }

    [Fact]
    public async Task Team_deletion_racing_accept_is_atomic_and_fully_classified()
    {
        await using var fixture = await InvitationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("team-race-owner@example.test");
        var recipient = await fixture.CreateUserAsync("team-race-recipient@example.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Team Accept Race");
        var team = await fixture.CreateTeamAsync(organization, "Soon Deleted");
        var session = await fixture.CreateSessionAsync(recipient);
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            owner,
            recipient.Email,
            team,
            expiresAt: InvitationStoreFixture.Now.AddMinutes(1));
        fixture.CoordinateNextMutationPair();

        var acceptTask = fixture.AcceptInvitationAsync(
            recipient,
            session,
            invitation);
        var deleteTask = fixture.DeleteTeamAsync(owner, organization, team);
        await Task.WhenAll(acceptTask, deleteTask);

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.True((await acceptTask).Succeeded);
        Assert.True((await deleteTask).Succeeded);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Teams.AnyAsync(
            row => row.Id == team.Value,
            TestContext.Current.CancellationToken));
        Assert.Null((await db.Invitations.SingleAsync(
            row => row.Id == invitation.Value,
            TestContext.Current.CancellationToken)).TeamId);
        Assert.False(await db.TeamMembers.AnyAsync(
            row => row.TeamId == team.Value,
            TestContext.Current.CancellationToken));
        Assert.True(await fixture.HasOrganizationMembershipAsync(
            organization,
            recipient));
    }
}
