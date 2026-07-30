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
        if (userMemberships.Length == 0)
        {
            return new OrganizationUserDeletionPreparation(
                DeletedOrganizations: 0,
                OwnershipTransferRequired: false);
        }

        var classifications = new List<OrganizationClassification>(
            userMemberships.Length);
        foreach (var membership in userMemberships)
        {
            await db.Organizations
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM organizations.organizations
                    WHERE id = {membership.OrganizationId}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken);

            var affectedMembers = await db.OrganizationMembers
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM organizations.members
                    WHERE organization_id = {membership.OrganizationId}
                    ORDER BY id
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
            classifications.Add(new OrganizationClassification(
                membership.OrganizationId,
                affectedMembers.Length,
                membership.Role == "owner",
                affectedMembers.Count(row => row.Role == "owner")));
        }

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
