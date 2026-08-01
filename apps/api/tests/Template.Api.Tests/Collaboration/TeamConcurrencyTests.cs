using Microsoft.EntityFrameworkCore;
using Template.Api.Tests.Infrastructure;
using Template.Application.Collaboration;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Tests.Collaboration;

public sealed class TeamConcurrencyTests(PostgreSqlContainerFixture postgres)
{
    [Theory]
    [InlineData(TeamMutationKind.Create)]
    [InlineData(TeamMutationKind.Update)]
    [InlineData(TeamMutationKind.Delete)]
    [InlineData(TeamMutationKind.AddMember)]
    [InlineData(TeamMutationKind.RemoveMember)]
    public async Task Every_team_mutation_retries_a_serialization_failure_with_a_fresh_transaction_and_authorization(
        TeamMutationKind kind)
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync(
            $"retry-{kind.ToString().ToLowerInvariant()}-owner@team.test");
        var target = await fixture.CreateUserAsync(
            $"retry-{kind.ToString().ToLowerInvariant()}-target@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            $"Retry {kind}");
        await fixture.AddOrganizationMemberAsync(
            organization,
            target,
            OrganizationRole.Member);
        var team = await fixture.SeedTeamAsync(organization, "Existing Team");
        if (kind == TeamMutationKind.RemoveMember)
        {
            await fixture.SeedTeamMemberAsync(organization, team, target);
        }

        fixture.FailNextMutationAfterAuthorization(
            kind,
            owner.UserId,
            failureCount: 1);

        var result = await ExecuteMutationAsync(
            fixture,
            kind,
            owner,
            organization,
            team,
            target);

        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(2, fixture.RetryOrganizationLockAttempts);
        Assert.Equal(2, fixture.RetryAuthorizationLockAttempts);
        Assert.Equal(2, fixture.RetryTransactionCount);
    }

    [Fact]
    public async Task Team_mutation_serialization_exhaustion_uses_exactly_three_fresh_attempts()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("retry-exhaust-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Retry Exhaustion");
        fixture.FailNextMutationAfterAuthorization(
            TeamMutationKind.Create,
            owner.UserId,
            failureCount: 3);

        var result = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, TeamNameFrom("Never Created")),
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.ConcurrencyConflict, result.Failure);
        Assert.Equal(3, fixture.RetryOrganizationLockAttempts);
        Assert.Equal(3, fixture.RetryAuthorizationLockAttempts);
        Assert.Equal(3, fixture.RetryTransactionCount);
        Assert.Equal(0, await fixture.CountTeamsAsync(organization));
    }

    [Fact]
    public async Task Team_mutation_permission_failure_is_not_retried()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("retry-permission-owner@team.test");
        var member = await fixture.CreateUserAsync("retry-permission-member@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Retry Permission");
        await fixture.AddOrganizationMemberAsync(
            organization,
            member,
            OrganizationRole.Member);
        fixture.FailNextMutationAfterAuthorization(
            TeamMutationKind.Create,
            member.UserId,
            failureCount: 1);

        var result = await fixture.Store.CreateAsync(
            new(member.UserId, organization, TeamNameFrom("Denied")),
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.PermissionDenied, result.Failure);
        Assert.Equal(1, fixture.RetryOrganizationLockAttempts);
        Assert.Equal(1, fixture.RetryAuthorizationLockAttempts);
        Assert.Equal(1, fixture.RetryTransactionCount);
    }

    [Fact]
    public async Task Team_mutation_validation_outcome_is_not_retried()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("retry-validation-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Retry Validation");
        var team = await fixture.SeedTeamAsync(organization, "Design");
        fixture.FailNextMutationAfterAuthorization(
            TeamMutationKind.Update,
            owner.UserId,
            failureCount: 0);

        var result = await fixture.Store.UpdateAsync(
            new(owner.UserId, organization, team, TeamNameFrom("design")),
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.NameUnchanged, result.Failure);
        Assert.Equal(1, fixture.RetryOrganizationLockAttempts);
        Assert.Equal(1, fixture.RetryAuthorizationLockAttempts);
        Assert.Equal(1, fixture.RetryTransactionCount);
    }

    [Fact]
    public async Task Team_mutation_unique_failure_is_classified_without_retry()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("retry-unique-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Retry Unique");
        fixture.FailNextTeamInsertWithUniqueViolation(owner.UserId);

        var result = await fixture.Store.CreateAsync(
            new(owner.UserId, organization, TeamNameFrom("Unique Race")),
            TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.NameConflict, result.Failure);
        Assert.Equal(1, fixture.RetryOrganizationLockAttempts);
        Assert.Equal(1, fixture.RetryAuthorizationLockAttempts);
        Assert.Equal(1, fixture.RetryTransactionCount);
        Assert.Equal(0, await fixture.CountTeamsAsync(organization));
    }

    [Fact]
    public async Task Team_mutation_cancellation_is_not_retried_or_mapped()
    {
        await using var fixture = await TeamStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("retry-cancel-owner@team.test");
        var organization = await fixture.CreateOrganizationAsync(
            owner,
            OrganizationRole.Owner,
            "Retry Cancellation");
        fixture.FailNextMutationAfterAuthorization(
            TeamMutationKind.Create,
            owner.UserId,
            failureCount: 1);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Store.CreateAsync(
                new(owner.UserId, organization, TeamNameFrom("Canceled")),
                canceled.Token));

        Assert.Equal(0, fixture.RetryOrganizationLockAttempts);
        Assert.Equal(0, fixture.RetryAuthorizationLockAttempts);
        Assert.Equal(0, fixture.RetryTransactionCount);
    }

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

    private static async Task<TeamMutationOutcome> ExecuteMutationAsync(
        TeamStoreFixture fixture,
        TeamMutationKind kind,
        TeamActor owner,
        OrganizationId organization,
        TeamId team,
        TeamActor target)
    {
        return kind switch
        {
            TeamMutationKind.Create => From(await fixture.Store.CreateAsync(
                new(owner.UserId, organization, TeamNameFrom("Created Team")),
                TestContext.Current.CancellationToken)),
            TeamMutationKind.Update => From(await fixture.Store.UpdateAsync(
                new(
                    owner.UserId,
                    organization,
                    team,
                    TeamNameFrom("Updated Team")),
                TestContext.Current.CancellationToken)),
            TeamMutationKind.Delete => From(await fixture.Store.DeleteAsync(
                new(owner.UserId, organization, team),
                TestContext.Current.CancellationToken)),
            TeamMutationKind.AddMember => From(await fixture.Store.AddMemberAsync(
                new(owner.UserId, organization, team, target.UserId),
                TestContext.Current.CancellationToken)),
            TeamMutationKind.RemoveMember => From(
                await fixture.Store.RemoveMemberAsync(
                    new(owner.UserId, organization, team, target.UserId),
                    TestContext.Current.CancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static TeamMutationOutcome From<T>(TeamOperationResult<T> result)
        where T : class => new(result.Succeeded, result.Failure);

    private static TeamName TeamNameFrom(string value)
    {
        Assert.True(TeamName.TryCreate(value, out var name));
        return name;
    }

    private sealed record TeamMutationOutcome(
        bool Succeeded,
        TeamFailure? Failure);
}
