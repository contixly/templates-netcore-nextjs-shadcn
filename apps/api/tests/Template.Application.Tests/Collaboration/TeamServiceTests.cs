using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Collaboration;

public sealed class TeamServiceTests
{
    private static readonly UserId Actor = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly UserId Target = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    private static readonly TeamId Team = new(Guid.Parse("00000000-0000-0000-0000-000000000020"));

    [Fact]
    public async Task Create_normalizes_before_calling_the_store()
    {
        var expected = TeamTestData.Summary();
        var store = new RecordingTeamStore
        {
            CreateResult = TeamOperationResult<TeamSummary>.Success(expected)
        };
        var service = new TeamService(store);

        var result = await service.CreateAsync(Actor, Organization, " Design ", TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(expected, result.Value);
        Assert.Equal("Design", store.LastCreate!.Name.Value);
        Assert.Equal(Actor, store.LastCreate.ActorUserId);
        Assert.Equal(Organization, store.LastCreate.OrganizationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad.Name")]
    [InlineData("Bad\tName")]
    public async Task Create_rejects_invalid_names_without_store_access(string name)
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        var result = await service.CreateAsync(Actor, Organization, name, TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.InvalidName, result.Failure);
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task Update_rejects_invalid_names_without_store_access()
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        var invalid = await service.UpdateAsync(Actor, Organization, Team, "Bad.Name", TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.InvalidName, invalid.Failure);
        Assert.Equal(0, store.UpdateCalls);
    }

    [Fact]
    public async Task Update_normalizes_before_calling_the_store()
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        await service.UpdateAsync(Actor, Organization, Team, " Design ", TestContext.Current.CancellationToken);

        Assert.Equal(new UpdateTeamCommand(Actor, Organization, Team, TeamNameFrom("Design")), store.LastUpdate);
    }

    [Fact]
    public async Task List_decodes_team_cursor_and_encodes_only_the_store_continuation()
    {
        var after = new TeamCursorPosition(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), Team);
        var next = new TeamCursorPosition(
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000021")));
        var store = new RecordingTeamStore
        {
            ListResult = TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>.Success(
                new([], next))
        };
        var service = new TeamService(store);

        var result = await service.ListAsync(Actor, Organization, TeamCursor.Encode(after), 25, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(Actor, store.LastListActor);
        Assert.Equal(Organization, store.LastListOrganization);
        Assert.Equal(after, store.LastListAfter);
        Assert.Equal(25, store.LastListLimit);
        Assert.True(TeamCursor.TryDecode(result.Value!.NextCursor, out TeamCursorPosition decoded));
        Assert.Equal(next, decoded);
    }

    [Fact]
    public async Task List_encodes_nested_member_continuations_from_store_positions()
    {
        var memberNext = new TeamMemberCursorPosition(
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            new TeamMemberId(Guid.Parse("00000000-0000-0000-0000-000000000031")));
        var store = new RecordingTeamStore
        {
            ListResult = TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>.Success(
                new([TeamTestData.StoreSummary(memberNext)], null))
        };
        var service = new TeamService(store);

        var result = await service.ListAsync(
            Actor,
            Organization,
            cursor: null,
            limit: 50,
            TestContext.Current.CancellationToken);

        var cursor = Assert.Single(result.Value!.Items).Members.NextCursor;
        Assert.NotNull(cursor);
        Assert.True(TeamCursor.TryDecode(cursor, out TeamMemberCursorPosition decoded));
        Assert.Equal(memberNext, decoded);
        Assert.False(TeamCursor.TryDecode(cursor, out TeamCursorPosition _));
    }

    [Fact]
    public async Task Invalid_cursor_is_rejected_without_store_access()
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        var result = await service.ListAsync(Actor, Organization, "broken", 50, TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.InvalidCursor, result.Failure);
        Assert.Equal(0, store.ListCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Lists_reject_out_of_range_limits(int limit)
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListAsync(Actor, Organization, null, limit, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListMembersAsync(Actor, Organization, Team, null, limit, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListCandidatesAsync(Actor, Organization, Team, null, null, limit, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.ListCalls);
        Assert.Equal(0, store.ListMemberCalls);
        Assert.Equal(0, store.ListCandidateCalls);
    }

    [Fact]
    public async Task Member_list_and_candidate_list_use_their_own_cursor_kinds()
    {
        var memberAfter = new TeamMemberCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new TeamMemberId(Guid.Parse("00000000-0000-0000-0000-000000000030")));
        var candidateAfter = new TeamCandidateCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            new OrganizationMemberId(Guid.Parse("00000000-0000-0000-0000-000000000040")));
        var memberNext = memberAfter with { Id = new TeamMemberId(Guid.Parse("00000000-0000-0000-0000-000000000031")) };
        var candidateNext = candidateAfter with { Id = new OrganizationMemberId(Guid.Parse("00000000-0000-0000-0000-000000000041")) };
        var store = new RecordingTeamStore
        {
            ListMembersResult = TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>.Success(
                new([], memberNext)),
            ListCandidatesResult = TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>.Success(
                new([], candidateNext))
        };
        var service = new TeamService(store);

        var members = await service.ListMembersAsync(Actor, Organization, Team, TeamCursor.Encode(memberAfter), 50, TestContext.Current.CancellationToken);
        var candidates = await service.ListCandidatesAsync(Actor, Organization, Team, "  Pat  ", TeamCursor.Encode(candidateAfter), 50, TestContext.Current.CancellationToken);

        Assert.True(members.Succeeded);
        Assert.True(candidates.Succeeded);
        Assert.Equal(memberAfter, store.LastListMemberAfter);
        Assert.Equal(candidateAfter, store.LastListCandidateAfter);
        Assert.Equal("Pat", store.LastCandidateQuery);
        Assert.True(TeamCursor.TryDecode(members.Value!.NextCursor, out TeamMemberCursorPosition decodedMember));
        Assert.True(TeamCursor.TryDecode(candidates.Value!.NextCursor, out TeamCandidateCursorPosition decodedCandidate));
        Assert.Equal(memberNext, decodedMember);
        Assert.Equal(candidateNext, decodedCandidate);
    }

    [Fact]
    public async Task Member_and_candidate_lists_reject_wrong_or_corrupt_cursors_without_store_access()
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);
        var teamCursor = TeamCursor.Encode(new TeamCursorPosition(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), Team));

        var member = await service.ListMembersAsync(Actor, Organization, Team, teamCursor, 50, TestContext.Current.CancellationToken);
        var candidate = await service.ListCandidatesAsync(Actor, Organization, Team, "", "broken", 50, TestContext.Current.CancellationToken);

        Assert.Equal(TeamFailure.InvalidCursor, member.Failure);
        Assert.Equal(TeamFailure.InvalidCursor, candidate.Failure);
        Assert.Equal(0, store.ListMemberCalls);
        Assert.Equal(0, store.ListCandidateCalls);
    }

    [Fact]
    public async Task Candidate_query_over_100_characters_is_rejected_without_store_access()
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListCandidatesAsync(
                Actor,
                Organization,
                Team,
                new string('a', 101),
                null,
                50,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, store.ListCandidateCalls);
    }

    [Theory]
    [InlineData(TeamFailure.InvalidName)]
    [InlineData(TeamFailure.InvalidCursor)]
    [InlineData(TeamFailure.NotFound)]
    [InlineData(TeamFailure.PermissionDenied)]
    [InlineData(TeamFailure.NameConflict)]
    [InlineData(TeamFailure.NameUnchanged)]
    [InlineData(TeamFailure.MemberNotFound)]
    [InlineData(TeamFailure.MemberAlreadyExists)]
    [InlineData(TeamFailure.ConcurrencyConflict)]
    public async Task Store_failures_are_propagated(TeamFailure failure)
    {
        var expected = TeamOperationResult<TeamSummary>.Failed(failure);
        var store = new RecordingTeamStore { CreateResult = expected };
        var service = new TeamService(store);

        var result = await service.CreateAsync(Actor, Organization, "Design", TestContext.Current.CancellationToken);

        Assert.Same(expected, result);
        Assert.Equal(failure, result.Failure);
    }

    [Fact]
    public async Task Delete_add_and_remove_pass_explicit_actor_organization_team_and_target_identifiers()
    {
        var store = new RecordingTeamStore();
        var service = new TeamService(store);

        await service.DeleteAsync(Actor, Organization, Team, TestContext.Current.CancellationToken);
        await service.AddMemberAsync(Actor, Organization, Team, Target, TestContext.Current.CancellationToken);
        await service.RemoveMemberAsync(Actor, Organization, Team, Target, TestContext.Current.CancellationToken);

        Assert.Equal(new DeleteTeamCommand(Actor, Organization, Team), store.LastDelete);
        Assert.Equal(new AddTeamMemberCommand(Actor, Organization, Team, Target), store.LastAddMember);
        Assert.Equal(new RemoveTeamMemberCommand(Actor, Organization, Team, Target), store.LastRemoveMember);
    }

    private static TeamName TeamNameFrom(string value)
    {
        Assert.True(TeamName.TryCreate(value, out var name));
        return name;
    }
}

internal static class TeamTestData
{
    internal static TeamSummary Summary()
    {
        Assert.True(TeamName.TryCreate("Design", out var name));
        return new TeamSummary(
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000020")),
            new OrganizationId(Guid.Parse("00000000-0000-0000-0000-000000000010")),
            name,
            0,
            new([], null),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
    }

    internal static TeamStoreSummary StoreSummary(TeamMemberCursorPosition? memberNext)
    {
        Assert.True(TeamName.TryCreate("Design", out var name));
        return new TeamStoreSummary(
            new TeamId(Guid.Parse("00000000-0000-0000-0000-000000000020")),
            new OrganizationId(Guid.Parse("00000000-0000-0000-0000-000000000010")),
            name,
            0,
            new([], memberNext),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
    }
}

internal sealed class RecordingTeamStore : ITeamStore
{
    public TeamOperationResult<TeamSummary> CreateResult { get; set; } = TeamOperationResult<TeamSummary>.Failed(TeamFailure.NotFound);
    public TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>> ListResult { get; set; } = TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>.Success(new([], null));
    public TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>> ListMembersResult { get; set; } = TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>.Success(new([], null));
    public TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>> ListCandidatesResult { get; set; } = TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>.Success(new([], null));
    public int ListCalls { get; private set; }
    public int CreateCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int ListMemberCalls { get; private set; }
    public int ListCandidateCalls { get; private set; }
    public UserId? LastListActor { get; private set; }
    public OrganizationId? LastListOrganization { get; private set; }
    public TeamCursorPosition? LastListAfter { get; private set; }
    public int? LastListLimit { get; private set; }
    public CreateTeamCommand? LastCreate { get; private set; }
    public UpdateTeamCommand? LastUpdate { get; private set; }
    public DeleteTeamCommand? LastDelete { get; private set; }
    public TeamMemberCursorPosition? LastListMemberAfter { get; private set; }
    public TeamCandidateCursorPosition? LastListCandidateAfter { get; private set; }
    public string? LastCandidateQuery { get; private set; }
    public AddTeamMemberCommand? LastAddMember { get; private set; }
    public RemoveTeamMemberCommand? LastRemoveMember { get; private set; }

    public Task<TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>> ListAsync(UserId actorUserId, OrganizationId organizationId, TeamCursorPosition? after, int limit, CancellationToken cancellationToken)
    {
        ListCalls++;
        LastListActor = actorUserId;
        LastListOrganization = organizationId;
        LastListAfter = after;
        LastListLimit = limit;
        return Task.FromResult(ListResult);
    }

    public Task<TeamOperationResult<TeamSummary>> CreateAsync(CreateTeamCommand command, CancellationToken cancellationToken)
    {
        CreateCalls++;
        LastCreate = command;
        return Task.FromResult(CreateResult);
    }

    public Task<TeamOperationResult<TeamSummary>> UpdateAsync(UpdateTeamCommand command, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        LastUpdate = command;
        return Task.FromResult(TeamOperationResult<TeamSummary>.Failed(TeamFailure.NotFound));
    }

    public Task<TeamOperationResult<TeamDeletion>> DeleteAsync(DeleteTeamCommand command, CancellationToken cancellationToken)
    {
        LastDelete = command;
        return Task.FromResult(TeamOperationResult<TeamDeletion>.Failed(TeamFailure.NotFound));
    }

    public Task<TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>> ListMembersAsync(UserId actorUserId, OrganizationId organizationId, TeamId teamId, TeamMemberCursorPosition? after, int limit, CancellationToken cancellationToken)
    {
        ListMemberCalls++;
        LastListMemberAfter = after;
        return Task.FromResult(ListMembersResult);
    }

    public Task<TeamOperationResult<TeamMemberView>> AddMemberAsync(AddTeamMemberCommand command, CancellationToken cancellationToken)
    {
        LastAddMember = command;
        return Task.FromResult(TeamOperationResult<TeamMemberView>.Failed(TeamFailure.NotFound));
    }

    public Task<TeamOperationResult<TeamMemberRemoval>> RemoveMemberAsync(RemoveTeamMemberCommand command, CancellationToken cancellationToken)
    {
        LastRemoveMember = command;
        return Task.FromResult(TeamOperationResult<TeamMemberRemoval>.Failed(TeamFailure.NotFound));
    }

    public Task<TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>> ListCandidatesAsync(UserId actorUserId, OrganizationId organizationId, TeamId teamId, string? query, TeamCandidateCursorPosition? after, int limit, CancellationToken cancellationToken)
    {
        ListCandidateCalls++;
        LastCandidateQuery = query;
        LastListCandidateAfter = after;
        return Task.FromResult(ListCandidatesResult);
    }
}
