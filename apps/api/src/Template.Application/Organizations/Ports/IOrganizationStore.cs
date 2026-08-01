using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Organizations.Ports;

public interface IOrganizationStore
{
    Task<OrganizationStorePage<OrganizationSummary, OrganizationListCursorPosition>>
        ListAsync(
            UserId actorUserId,
            OrganizationListCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<OrganizationOperationResult<OrganizationDetail>> GetByKeyAsync(
        UserId actorUserId,
        string organizationKey,
        CancellationToken cancellationToken);

    Task<OrganizationOperationResult<OrganizationDetail>> CreateAsync(
        CreateOrganizationCommand command,
        CancellationToken cancellationToken);

    Task<OrganizationOperationResult<OrganizationDetail>> UpdateAsync(
        UpdateOrganizationCommand command,
        CancellationToken cancellationToken);

    Task<OrganizationOperationResult<OrganizationDeletion>> DeleteAsync(
        DeleteOrganizationCommand command,
        CancellationToken cancellationToken);

    Task<OrganizationOperationResult<ActiveOrganization>> SetActiveAsync(
        SetActiveOrganizationCommand command,
        CancellationToken cancellationToken);

    Task<OrganizationOperationResult<
        OrganizationStorePage<
            OrganizationMember,
            OrganizationMemberCursorPosition>>>
        ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken);

    Task<OrganizationOperationResult<OrganizationMember>> AddMemberAsync(
        AddOrganizationMemberCommand command,
        CancellationToken cancellationToken);

    Task<OrganizationOperationResult<OrganizationMember>> UpdateMemberRoleAsync(
        UpdateOrganizationMemberRoleCommand command,
        CancellationToken cancellationToken);
}
