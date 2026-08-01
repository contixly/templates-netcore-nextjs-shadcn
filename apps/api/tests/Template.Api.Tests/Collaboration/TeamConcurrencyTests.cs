using Microsoft.EntityFrameworkCore;
using Template.Api.Tests.Infrastructure;
using Template.Application.Collaboration;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Tests.Collaboration;

public sealed class TeamConcurrencyTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Concurrent_same_name_creates_have_one_classified_loser()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("create-race-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Create Race");
        fixture.CoordinateNextMutationPair();

        var attempts = await Task.WhenAll(
            fixture.CreateTeamAsync(owner, organization, "Design"),
            fixture.CreateTeamAsync(owner, organization, "dESIGN"));

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == TeamFailure.NameConflict));
        Assert.Equal(1, await fixture.CountTeamsAsync(organization));
    }

    [Fact]
    public async Task Concurrent_duplicate_adds_have_one_classified_loser()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("add-race-owner@team.test");
        var target = await fixture.CreateUserAsync("add-race-target@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Add Race");
        await fixture.AddOrganizationMemberAsync(
            organization,
            target,
            OrganizationRole.Member);
        var team = await fixture.SeedTeamAsync(organization, "Race Team");
        fixture.CoordinateNextMutationPair();

        var attempts = await Task.WhenAll(
            fixture.AddTeamMemberAsync(owner, organization, team, target),
            fixture.AddTeamMemberAsync(owner, organization, team, target));

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == TeamFailure.MemberAlreadyExists));
        Assert.Equal(1, await fixture.CountTeamMembersAsync(team));
    }

    [Fact]
    public async Task Concurrent_rename_and_delete_cannot_leave_an_invitation_orphan()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("rename-delete-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Rename Delete Race");
        var team = await fixture.SeedTeamAsync(organization, "Before");
        var invitation = await fixture.SeedInvitationAsync(
            organization,
            team,
            owner,
            "rename-delete-invitee@team.test");
        fixture.CoordinateNextMutationPair();

        var rename = fixture.UpdateTeamAsync(
            owner,
            organization,
            team,
            "After");
        var delete = fixture.DeleteTeamAsync(owner, organization, team);
        await Task.WhenAll(rename, delete);

        Assert.True(fixture.MutationPairWasCoordinated);
        Assert.True((await delete).Succeeded);
        Assert.True(
            (await rename).Succeeded ||
            (await rename).Failure == TeamFailure.NotFound);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Teams.AnyAsync(
            row => row.Id == team.Value,
            TestContext.Current.CancellationToken));
        Assert.Null((await db.Invitations.SingleAsync(
            row => row.Id == invitation,
            TestContext.Current.CancellationToken)).TeamId);
    }
}
