using Template.Application.Organizations;
using Template.Domain.Authentication;
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
}
