using Microsoft.EntityFrameworkCore;
using Template.Api.Tests.Infrastructure;
using Template.Application.Organizations;
using Template.Domain.Organizations;

namespace Template.Api.Tests.Organizations;

public sealed class OrganizationConcurrencyTests(
    PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Last_owner_race_allows_at_most_one_demotion()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateWithTwoOwnersAsync(postgres);

        var attempts = await Task.WhenAll(
            fixture.DemoteOwnerAsync(fixture.FirstOwner),
            fixture.DemoteOwnerAsync(fixture.SecondOwner));

        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(1, attempts.Count(result =>
            result.Failure == OrganizationFailure.RoleAssignmentForbidden ||
            result.Failure == OrganizationFailure.ConcurrencyConflict));
        Assert.Equal(1, await fixture.CountOwnersAsync());
    }

    [Fact]
    public async Task Slug_unique_race_retries_with_a_suffix()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var first = await fixture.CreateUserAndSessionAsync(
            "slug-race-first@local-agent.test");
        var second = await fixture.CreateUserAndSessionAsync(
            "slug-race-second@local-agent.test");

        var attempts = await Task.WhenAll(
            fixture.CreateOrganizationAsync(first, "Collision"),
            fixture.CreateOrganizationAsync(second, "Collision"));

        Assert.All(attempts, result => Assert.True(result.Succeeded));
        Assert.Equal(
            ["collision", "collision-2"],
            attempts
                .Select(result => Assert.IsType<OrganizationDetail>(result.Value)
                    .Slug.Value)
                .Order(StringComparer.Ordinal)
                .ToArray());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            2,
            await db.Organizations.CountAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Same_actor_create_race_is_idempotent_at_the_name_boundary()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "name-race@local-agent.test");

        var attempts = await Task.WhenAll(
            fixture.CreateOrganizationAsync(actor, "Same Name"),
            fixture.CreateOrganizationAsync(actor, "same name"));

        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == OrganizationFailure.NameConflict));
        Assert.Equal(1, await fixture.CountAccessibleOrganizationsAsync(actor));
    }

    [Fact]
    public async Task Concurrent_renames_share_one_case_insensitive_actor_namespace()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "rename-race@local-agent.test");
        var first = await fixture.SeedOrganizationForAsync(
            actor,
            "Rename First",
            "rename-first",
            OrganizationRole.Owner);
        var second = await fixture.SeedOrganizationForAsync(
            actor,
            "Rename Second",
            "rename-second",
            OrganizationRole.Owner);
        fixture.CoordinateConcurrentNameChecks();

        var attempts = await Task.WhenAll(
            fixture.UpdateOrganizationAsync(actor, first, "Shared Name"),
            fixture.UpdateOrganizationAsync(actor, second, "sHARED nAME"));

        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == OrganizationFailure.NameConflict));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            1,
            await db.Organizations.CountAsync(
                row => row.Name.ToLower() == "shared name",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_and_create_share_one_case_insensitive_actor_namespace()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAndSessionAsync(
            "update-create-race@local-agent.test");
        var target = await fixture.SeedOrganizationForAsync(
            actor,
            "Update Target",
            "update-target",
            OrganizationRole.Owner);
        fixture.CoordinateConcurrentNameChecks();

        var update = fixture.UpdateOrganizationAsync(
            actor,
            target,
            "Shared Name");
        var create = fixture.CreateOrganizationAsync(
            actor,
            "sHARED nAME");
        await Task.WhenAll(update, create);

        var attempts = new[]
        {
            await update,
            await create
        };
        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == OrganizationFailure.NameConflict));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(
            1,
            await db.Organizations.CountAsync(
                row => row.Name.ToLower() == "shared name",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_deletes_preserve_one_accessible_organization()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "delete-race@local-agent.test");
        var first = await fixture.SeedOrganizationForAsync(
            owner,
            "First",
            "first",
            OrganizationRole.Owner);
        var second = await fixture.SeedOrganizationForAsync(
            owner,
            "Second",
            "second",
            OrganizationRole.Owner);

        var attempts = await Task.WhenAll(
            fixture.DeleteOrganizationAsync(owner, first, "First"),
            fixture.DeleteOrganizationAsync(owner, second, "Second"));

        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(1, attempts.Count(result =>
            result.Failure == OrganizationFailure.LastAccessibleOrganization ||
            result.Failure == OrganizationFailure.ConcurrencyConflict));
        Assert.Equal(1, await fixture.CountAccessibleOrganizationsAsync(owner));
    }

    [Fact]
    public async Task Set_active_and_delete_race_serializes_without_database_failure()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "set-active-delete-race@local-agent.test");
        var target = await fixture.SeedOrganizationForAsync(
            owner,
            "Delete Target",
            "delete-target",
            OrganizationRole.Owner);
        await fixture.SeedOrganizationForAsync(
            owner,
            "Remaining",
            "remaining",
            OrganizationRole.Owner);

        await using var sessionLock =
            await fixture.LockSessionAsync(owner.SessionId);
        var selection = fixture.SetActiveOrganizationAsync(owner, target);
        await fixture.WaitForSessionUpdateLockAsync();
        var deletion = fixture.DeleteOrganizationAsync(
            owner,
            target,
            "Delete Target");
        await fixture.WaitForOrganizationLockAsync();
        await sessionLock.ReleaseAsync();

        var deleted = await deletion;
        var selected = await selection;

        Assert.True(deleted.Succeeded);
        Assert.True(selected.Succeeded);
    }

    [Fact]
    public async Task Acknowledged_duplicate_member_race_inserts_exactly_once()
    {
        await using var fixture =
            await OrganizationStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAndSessionAsync(
            "member-race-owner@local-agent.test");
        var target = await fixture.CreateUserAndSessionAsync(
            "member-race-target@local-agent.test");
        var organizationId = await fixture.SeedOrganizationForAsync(
            owner,
            "Member Race",
            "member-race",
            OrganizationRole.Owner);

        var attempts = await Task.WhenAll(
            fixture.AddMemberAsync(
                owner,
                organizationId,
                target,
                OrganizationRole.Member),
            fixture.AddMemberAsync(
                owner,
                organizationId,
                target,
                OrganizationRole.Member));

        Assert.Equal(1, attempts.Count(result => result.Succeeded));
        Assert.Equal(
            1,
            attempts.Count(result =>
                result.Failure == OrganizationFailure.MemberAlreadyExists));
        Assert.Equal(
            1,
            await fixture.CountMembershipsAsync(
                organizationId,
                target.UserId));
    }
}
