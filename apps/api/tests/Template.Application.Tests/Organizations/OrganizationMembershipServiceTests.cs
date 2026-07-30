using Template.Application.Organizations;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Organizations;

public sealed class OrganizationMembershipServiceTests
{
    private static readonly UserId Actor =
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly UserId Target =
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"));
    private static readonly OrganizationId Organization =
        new(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    private static readonly OrganizationMemberId Member =
        new(Guid.Parse("00000000-0000-0000-0000-000000000020"));

    [Fact]
    public async Task Invalid_member_cursor_is_rejected_without_store_access()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationMembershipService(store);

        var result = await service.ListAsync(
            Actor,
            Organization,
            "broken",
            50,
            TestContext.Current.CancellationToken);

        Assert.Equal(OrganizationFailure.InvalidCursor, result.Failure);
        Assert.Equal(0, store.ListMemberCalls);
    }

    [Fact]
    public async Task List_decodes_input_and_encodes_the_store_continuation()
    {
        var after = new OrganizationMemberCursorPosition(
            DateTimeOffset.Parse("2026-07-30T00:00:00Z"),
            Member);
        var next = new OrganizationMemberCursorPosition(
            DateTimeOffset.Parse("2026-07-31T00:00:00Z"),
            new OrganizationMemberId(
                Guid.Parse("00000000-0000-0000-0000-000000000021")));
        var store = new RecordingOrganizationStore
        {
            ListMembersResult = new OrganizationStorePage<
                OrganizationMember,
                OrganizationMemberCursorPosition>([], next)
        };
        var service = new OrganizationMembershipService(store);

        var result = await service.ListAsync(
            Actor,
            Organization,
            OrganizationCursor.Encode(after),
            75,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(Actor, store.LastMemberListActor);
        Assert.Equal(Organization, store.LastMemberListOrganizationId);
        Assert.Equal(after, store.LastMemberListAfter);
        Assert.Equal(75, store.LastMemberListLimit);
        Assert.True(OrganizationCursor.TryDecode(
            result.Value!.NextCursor!,
            out OrganizationMemberCursorPosition decoded));
        Assert.Equal(next, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_rejects_out_of_range_limits(int limit)
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationMembershipService(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListAsync(Actor, Organization, null, limit, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.ListMemberCalls);
    }

    [Fact]
    public async Task Outside_domain_add_surfaces_the_acknowledgement_result()
    {
        var acknowledgement = new OrganizationDomainAcknowledgement(
            "target@outside.test",
            "outside.test",
            ["example.com"]);
        var expected = OrganizationOperationResult<OrganizationMember>.Failed(
            OrganizationFailure.DomainAcknowledgementRequired,
            acknowledgement);
        var store = new RecordingOrganizationStore { AddMemberResult = expected };
        var service = new OrganizationMembershipService(store);

        var result = await service.AddAsync(
            Actor,
            Organization,
            Target,
            OrganizationRole.Member,
            acknowledgeDomainRestriction: false,
            TestContext.Current.CancellationToken);

        Assert.Same(expected, result);
        Assert.Same(acknowledgement, result.Acknowledgement);
        Assert.Equal(OrganizationFailure.DomainAcknowledgementRequired, result.Failure);
    }

    [Fact]
    public async Task Add_and_update_role_pass_every_actor_and_target_identifier()
    {
        var store = new RecordingOrganizationStore();
        var service = new OrganizationMembershipService(store);

        await service.AddAsync(
            Actor,
            Organization,
            Target,
            OrganizationRole.Admin,
            acknowledgeDomainRestriction: true,
            TestContext.Current.CancellationToken);
        await service.UpdateRoleAsync(
            Actor,
            Organization,
            Member,
            OrganizationRole.Member,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new AddOrganizationMemberCommand(
                Actor,
                Organization,
                Target,
                OrganizationRole.Admin,
                true),
            store.LastAddMember);
        Assert.Equal(
            new UpdateOrganizationMemberRoleCommand(
                Actor,
                Organization,
                Member,
                OrganizationRole.Member),
            store.LastUpdateRole);
    }
}
