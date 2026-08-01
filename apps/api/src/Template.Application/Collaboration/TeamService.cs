using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Collaboration;

public sealed class TeamService(ITeamStore teams)
{
    public async Task<TeamOperationResult<TeamPage>> ListAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, "Team page limit must be between 1 and 100.");
        if (!TryDecode(cursor, out TeamCursorPosition? after))
        {
            return TeamOperationResult<TeamPage>.Failed(TeamFailure.InvalidCursor);
        }

        var result = await teams.ListAsync(actorUserId, organizationId, after, limit, cancellationToken);
        if (!result.Succeeded)
        {
            return TeamOperationResult<TeamPage>.Failed(RequireFailure(result));
        }

        var page = RequireValue(result);
        return TeamOperationResult<TeamPage>.Success(new(
            page.Items,
            page.Next is null ? null : TeamCursor.Encode(page.Next)));
    }

    public Task<TeamOperationResult<TeamSummary>> CreateAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        string? name,
        CancellationToken cancellationToken)
    {
        if (!TeamName.TryCreate(name, out var normalizedName))
        {
            return Task.FromResult(TeamOperationResult<TeamSummary>.Failed(TeamFailure.InvalidName));
        }

        return teams.CreateAsync(new(actorUserId, organizationId, normalizedName), cancellationToken);
    }

    public Task<TeamOperationResult<TeamSummary>> UpdateAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        string? name,
        CancellationToken cancellationToken)
    {
        if (!TeamName.TryCreate(name, out var normalizedName))
        {
            return Task.FromResult(TeamOperationResult<TeamSummary>.Failed(TeamFailure.InvalidName));
        }

        return teams.UpdateAsync(new(actorUserId, organizationId, teamId, normalizedName), cancellationToken);
    }

    public Task<TeamOperationResult<TeamDeletion>> DeleteAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        CancellationToken cancellationToken) =>
        teams.DeleteAsync(new(actorUserId, organizationId, teamId), cancellationToken);

    public async Task<TeamOperationResult<TeamMemberPage>> ListMembersAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, "Team member page limit must be between 1 and 100.");
        if (!TryDecode(cursor, out TeamMemberCursorPosition? after))
        {
            return TeamOperationResult<TeamMemberPage>.Failed(TeamFailure.InvalidCursor);
        }

        var result = await teams.ListMembersAsync(
            actorUserId,
            organizationId,
            teamId,
            after,
            limit,
            cancellationToken);
        if (!result.Succeeded)
        {
            return TeamOperationResult<TeamMemberPage>.Failed(RequireFailure(result));
        }

        var page = RequireValue(result);
        return TeamOperationResult<TeamMemberPage>.Success(new(
            page.Items,
            page.Next is null ? null : TeamCursor.Encode(page.Next)));
    }

    public Task<TeamOperationResult<TeamMemberView>> AddMemberAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        UserId targetUserId,
        CancellationToken cancellationToken) =>
        teams.AddMemberAsync(new(actorUserId, organizationId, teamId, targetUserId), cancellationToken);

    public Task<TeamOperationResult<TeamMemberRemoval>> RemoveMemberAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        UserId targetUserId,
        CancellationToken cancellationToken) =>
        teams.RemoveMemberAsync(new(actorUserId, organizationId, teamId, targetUserId), cancellationToken);

    public async Task<TeamOperationResult<TeamCandidatePage>> ListCandidatesAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        string? query,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, "Team candidate page limit must be between 1 and 100.");
        var normalizedQuery = query?.Trim();
        if (normalizedQuery is { Length: > 100 })
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Team candidate query must be at most 100 characters.");
        }

        if (!TryDecode(cursor, out TeamCandidateCursorPosition? after))
        {
            return TeamOperationResult<TeamCandidatePage>.Failed(TeamFailure.InvalidCursor);
        }

        var result = await teams.ListCandidatesAsync(
            actorUserId,
            organizationId,
            teamId,
            normalizedQuery,
            after,
            limit,
            cancellationToken);
        if (!result.Succeeded)
        {
            return TeamOperationResult<TeamCandidatePage>.Failed(RequireFailure(result));
        }

        var page = RequireValue(result);
        return TeamOperationResult<TeamCandidatePage>.Success(new(
            page.Items,
            page.Next is null ? null : TeamCursor.Encode(page.Next)));
    }

    private static bool TryDecode<TPosition>(string? cursor, out TPosition? after)
        where TPosition : class
    {
        after = default;
        if (cursor is null)
        {
            return true;
        }

        if (typeof(TPosition) == typeof(TeamCursorPosition)
            && TeamCursor.TryDecode(cursor, out TeamCursorPosition team))
        {
            after = (TPosition)(object)team;
            return true;
        }

        if (typeof(TPosition) == typeof(TeamMemberCursorPosition)
            && TeamCursor.TryDecode(cursor, out TeamMemberCursorPosition member))
        {
            after = (TPosition)(object)member;
            return true;
        }

        if (typeof(TPosition) == typeof(TeamCandidateCursorPosition)
            && TeamCursor.TryDecode(cursor, out TeamCandidateCursorPosition candidate))
        {
            after = (TPosition)(object)candidate;
            return true;
        }

        return false;
    }

    private static TeamFailure RequireFailure<T>(TeamOperationResult<T> result)
        where T : class =>
        result.Failure ?? throw new InvalidOperationException("A failed team result requires a failure.");

    private static T RequireValue<T>(TeamOperationResult<T> result)
        where T : class =>
        result.Value ?? throw new InvalidOperationException("A successful team result requires a value.");

    private static void ValidateLimit(int limit, string message)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), message);
        }
    }
}
