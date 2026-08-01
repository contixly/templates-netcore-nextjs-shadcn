namespace Template.Domain.Organizations;

public sealed record OrganizationCapabilities(
    bool CanUpdateOrganization,
    bool CanDeleteOrganization,
    bool CanAddMembers,
    bool CanUpdateMemberRoles,
    bool CanManageTeams,
    bool CanManageInvitations);

public static class OrganizationPermissionPolicy
{
    public static OrganizationCapabilities GetCapabilities(OrganizationRole role) => role switch
    {
        var value when value == OrganizationRole.Admin => new(true, false, true, true, true, true),
        var value when value == OrganizationRole.Owner => new(true, true, true, true, true, true),
        _ => new(false, false, false, false, false, false)
    };

    public static bool CanAssign(OrganizationRole actorRole, OrganizationRole requestedRole) => actorRole switch
    {
        var value when value == OrganizationRole.Admin =>
            requestedRole == OrganizationRole.Member || requestedRole == OrganizationRole.Admin,
        var value when value == OrganizationRole.Owner =>
            requestedRole == OrganizationRole.Member ||
            requestedRole == OrganizationRole.Admin ||
            requestedRole == OrganizationRole.Owner,
        _ => false
    };

    public static bool CanChangeRole(
        OrganizationRole actorRole,
        Guid actorUserId,
        Guid targetUserId,
        OrganizationRole currentTargetRole,
        OrganizationRole requestedRole,
        int ownerCount)
    {
        if (actorUserId == targetUserId || currentTargetRole == requestedRole)
        {
            return false;
        }

        if (actorRole == OrganizationRole.Admin && currentTargetRole == OrganizationRole.Owner)
        {
            return false;
        }

        if (!CanAssign(actorRole, requestedRole))
        {
            return false;
        }

        return currentTargetRole != OrganizationRole.Owner ||
            requestedRole == OrganizationRole.Owner ||
            ownerCount > 1;
    }
}
