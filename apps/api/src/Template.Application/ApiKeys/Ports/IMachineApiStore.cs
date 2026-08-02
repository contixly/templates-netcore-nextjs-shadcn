using Template.Application.Organizations;
using Template.Application.Collaboration;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.ApiKeys.Ports;

public interface IMachineApiStore
{
    Task<OrganizationStorePage<
        MachineOrganizationSummary,
        OrganizationListCursorPosition>> ListUserOrganizationsAsync(
            UserId userId,
            OrganizationListCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<MachineOrganizationSummary?> GetUserOrganizationAsync(
        UserId userId,
        OrganizationId organizationId,
        CancellationToken cancellationToken);

    Task<MachineOrganizationSummary?> GetOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken);

    Task<OrganizationStorePage<
        OrganizationMember,
        OrganizationMemberCursorPosition>?> ListUserOrganizationMembersAsync(
            UserId userId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<OrganizationStorePage<
        OrganizationMember,
        OrganizationMemberCursorPosition>?> ListOrganizationMembersAsync(
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<TeamStorePage<
        MachineTeamStoreSummary,
        TeamCursorPosition>?> ListUserOrganizationTeamsAsync(
            UserId userId,
            OrganizationId organizationId,
            TeamCursorPosition? after,
            int limit,
            bool includeMembers,
            CancellationToken cancellationToken);

    Task<TeamStorePage<
        MachineTeamStoreSummary,
        TeamCursorPosition>?> ListOrganizationTeamsAsync(
            OrganizationId organizationId,
            TeamCursorPosition? after,
            int limit,
            bool includeMembers,
            CancellationToken cancellationToken);

    Task<TeamStorePage<
        TeamMemberView,
        TeamMemberCursorPosition>?> ListUserOrganizationTeamMembersAsync(
            UserId userId,
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<TeamStorePage<
        TeamMemberView,
        TeamMemberCursorPosition>?> ListOrganizationTeamMembersAsync(
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);
}
