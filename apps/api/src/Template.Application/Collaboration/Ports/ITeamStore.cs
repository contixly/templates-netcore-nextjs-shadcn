using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Collaboration.Ports;

public interface ITeamStore
{
    Task<TeamOperationResult<TeamStorePage<TeamStoreSummary, TeamCursorPosition>>> ListAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamCursorPosition? after,
        int limit,
        CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamSummary>> CreateAsync(
        CreateTeamCommand command,
        CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamSummary>> UpdateAsync(
        UpdateTeamCommand command,
        CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamDeletion>> DeleteAsync(
        DeleteTeamCommand command,
        CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>>
        ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamMemberView>> AddMemberAsync(
        AddTeamMemberCommand command,
        CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamMemberRemoval>> RemoveMemberAsync(
        RemoveTeamMemberCommand command,
        CancellationToken cancellationToken);

    Task<TeamOperationResult<TeamStorePage<TeamCandidate, TeamCandidateCursorPosition>>>
        ListCandidatesAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            TeamId teamId,
            string? query,
            TeamCandidateCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);
}
