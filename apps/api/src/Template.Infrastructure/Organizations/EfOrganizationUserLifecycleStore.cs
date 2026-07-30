using Microsoft.EntityFrameworkCore;
using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Organizations;

internal sealed class EfOrganizationUserLifecycleStore(TemplateDbContext db)
    : IOrganizationUserLifecycleStore
{
    public async Task<OrganizationUserDeletionPreparation>
        PrepareDeletionAsync(
            UserId userId,
            CancellationToken cancellationToken)
    {
        var discoveredOrganizationIds = await db.OrganizationMembers
            .AsNoTracking()
            .Where(membership => membership.UserId == userId.Value)
            .OrderBy(membership => membership.OrganizationId)
            .Select(membership => membership.OrganizationId)
            .ToArrayAsync(cancellationToken);

        if (discoveredOrganizationIds.Length > 0)
        {
            var lockedOrganizations = await db.Organizations
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM organizations.organizations
                    WHERE id = ANY ({discoveredOrganizationIds})
                    ORDER BY id
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
            var lockedOrganizationIds = lockedOrganizations
                .Select(organization => organization.Id)
                .ToArray();
            if (!lockedOrganizationIds.SequenceEqual(
                    discoveredOrganizationIds))
            {
                throw new OrganizationUserLifecycleConcurrencyException();
            }
        }

        var user = await db.Users
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM auth.users
                WHERE id = {userId.Value}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return new OrganizationUserDeletionPreparation(
                DeletedOrganizations: 0,
                OwnershipTransferRequired: false);
        }

        var userMemberships = await db.OrganizationMembers
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM organizations.members
                WHERE user_id = {userId.Value}
                ORDER BY organization_id, id
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var currentOrganizationIds = userMemberships
            .Select(membership => membership.OrganizationId)
            .ToArray();
        if (!currentOrganizationIds.SequenceEqual(discoveredOrganizationIds))
        {
            throw new OrganizationUserLifecycleConcurrencyException();
        }

        if (currentOrganizationIds.Length == 0)
        {
            return new OrganizationUserDeletionPreparation(
                DeletedOrganizations: 0,
                OwnershipTransferRequired: false);
        }

        var affectedMembers = await db.OrganizationMembers
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM organizations.members
                WHERE organization_id = ANY ({currentOrganizationIds})
                ORDER BY organization_id, id
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var membershipsByOrganization = affectedMembers
            .GroupBy(membership => membership.OrganizationId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var classifications = userMemberships
            .Select(membership =>
            {
                var organizationMembers =
                    membershipsByOrganization[membership.OrganizationId];
                return new OrganizationClassification(
                    membership.OrganizationId,
                    organizationMembers.Length,
                    membership.Role == "owner",
                    organizationMembers.Count(row => row.Role == "owner"));
            })
            .ToArray();

        if (classifications.Any(classification =>
                classification.MemberCount > 1
                && classification.UserIsOwner
                && classification.OwnerCount == 1))
        {
            return new OrganizationUserDeletionPreparation(
                DeletedOrganizations: 0,
                OwnershipTransferRequired: true);
        }

        var affectedOrganizationIds = classifications
            .Select(classification => classification.OrganizationId)
            .ToArray();
        await db.Sessions
            .Where(session =>
                session.UserId == userId.Value
                && session.ActiveOrganizationId != null
                && affectedOrganizationIds.Contains(
                    session.ActiveOrganizationId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.ActiveOrganizationId,
                    (Guid?)null),
                cancellationToken);

        var singleMemberOrganizationIds = classifications
            .Where(classification => classification.MemberCount == 1)
            .Select(classification => classification.OrganizationId)
            .ToArray();
        if (singleMemberOrganizationIds.Length > 0)
        {
            await db.Organizations
                .Where(organization =>
                    singleMemberOrganizationIds.Contains(organization.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return new OrganizationUserDeletionPreparation(
            DeletedOrganizations: singleMemberOrganizationIds.Length,
            OwnershipTransferRequired: false);
    }

    private sealed record OrganizationClassification(
        Guid OrganizationId,
        int MemberCount,
        bool UserIsOwner,
        int OwnerCount);
}
