using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Organizations;

public sealed class OrganizationMembershipService(IOrganizationStore organizations)
{
    public async Task<OrganizationOperationResult<OrganizationMemberPage>> ListAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit);

        OrganizationMemberCursorPosition? after = null;
        if (cursor is not null)
        {
            if (!OrganizationCursor.TryDecode(
                    cursor,
                    out OrganizationMemberCursorPosition decoded))
            {
                return OrganizationOperationResult<OrganizationMemberPage>.Failed(
                    OrganizationFailure.InvalidCursor);
            }

            after = decoded;
        }

        var page = await organizations.ListMembersAsync(
            actorUserId,
            organizationId,
            after,
            limit,
            cancellationToken);
        return OrganizationOperationResult<OrganizationMemberPage>.Success(
            new OrganizationMemberPage(
                page.Items,
                page.Next is null ? null : OrganizationCursor.Encode(page.Next)));
    }

    public Task<OrganizationOperationResult<OrganizationMember>> AddAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        UserId targetUserId,
        OrganizationRole role,
        bool acknowledgeDomainRestriction,
        CancellationToken cancellationToken) =>
        organizations.AddMemberAsync(
            new AddOrganizationMemberCommand(
                actorUserId,
                organizationId,
                targetUserId,
                role,
                acknowledgeDomainRestriction),
            cancellationToken);

    public Task<OrganizationOperationResult<OrganizationMember>> UpdateRoleAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        OrganizationMemberId memberId,
        OrganizationRole role,
        CancellationToken cancellationToken) =>
        organizations.UpdateMemberRoleAsync(
            new UpdateOrganizationMemberRoleCommand(
                actorUserId,
                organizationId,
                memberId,
                role),
            cancellationToken);

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Organization member page limit must be between 1 and 100.");
        }
    }
}
