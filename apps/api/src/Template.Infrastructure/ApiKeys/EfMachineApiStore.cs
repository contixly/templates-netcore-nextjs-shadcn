using Microsoft.EntityFrameworkCore;
using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Application.Organizations;
using Template.Domain.Authentication;
using Template.Domain.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.ApiKeys;

internal sealed class EfMachineApiStore(TemplateDbContext db)
    : IMachineApiStore
{
    private static readonly OrganizationCapabilities OrganizationCapabilities =
        new(false, false, false, false, false, false, false);

    public async Task<OrganizationStorePage<
        MachineOrganizationSummary,
        OrganizationListCursorPosition>> ListUserOrganizationsAsync(
            UserId userId,
            OrganizationListCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var query =
            from organization in db.Organizations.AsNoTracking()
            join membership in db.OrganizationMembers.AsNoTracking()
                on organization.Id equals membership.OrganizationId
            where membership.UserId == userId.Value
            select new
            {
                organization.Id,
                organization.Name,
                organization.Slug,
                organization.CreatedAt,
                organization.UpdatedAt,
                MembershipId = membership.Id,
                MembershipJoinedAt = membership.JoinedAt,
                membership.Role
            };

        if (after is not null)
        {
            query = query.Where(row =>
                row.MembershipJoinedAt > after.MembershipJoinedAt ||
                row.MembershipJoinedAt == after.MembershipJoinedAt &&
                row.MembershipId.CompareTo(after.MembershipId.Value) > 0);
        }

        var rows = await query
            .OrderBy(row => row.MembershipJoinedAt)
            .ThenBy(row => row.MembershipId)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var pageRows = hasMore ? rows[..limit] : rows;
        var items = pageRows.Select(row => MapUserOrganization(
            row.Id,
            row.Name,
            row.Slug,
            row.CreatedAt,
            row.UpdatedAt,
            row.Role)).ToArray();
        var next = hasMore
            ? new OrganizationListCursorPosition(
                pageRows[^1].MembershipJoinedAt,
                new OrganizationMemberId(pageRows[^1].MembershipId))
            : null;
        return new(items, next);
    }

    public async Task<MachineOrganizationSummary?> GetUserOrganizationAsync(
        UserId userId,
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var row = await (
                from organization in db.Organizations.AsNoTracking()
                join membership in db.OrganizationMembers.AsNoTracking()
                    on organization.Id equals membership.OrganizationId
                where organization.Id == organizationId.Value &&
                      membership.UserId == userId.Value
                select new
                {
                    organization.Id,
                    organization.Name,
                    organization.Slug,
                    organization.CreatedAt,
                    organization.UpdatedAt,
                    membership.Role
                })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : MapUserOrganization(
                row.Id,
                row.Name,
                row.Slug,
                row.CreatedAt,
                row.UpdatedAt,
                row.Role);
    }

    public async Task<MachineOrganizationSummary?> GetOrganizationAsync(
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var row = await db.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId.Value)
            .Select(organization => new
            {
                organization.Id,
                organization.Name,
                organization.Slug,
                organization.CreatedAt,
                organization.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : new(
                new OrganizationId(row.Id),
                row.Name,
                row.Slug,
                row.CreatedAt,
                row.UpdatedAt,
                "organization",
                "organization",
                OrganizationCapabilities);
    }

    public async Task<OrganizationStorePage<
        OrganizationMember,
        OrganizationMemberCursorPosition>?> ListUserOrganizationMembersAsync(
            UserId userId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var accessible = await db.OrganizationMembers.AsNoTracking().AnyAsync(
            membership =>
                membership.OrganizationId == organizationId.Value &&
                membership.UserId == userId.Value,
            cancellationToken);
        return accessible
            ? await ListMembersCoreAsync(
                organizationId,
                userId,
                after,
                limit,
                cancellationToken)
            : null;
    }

    public async Task<OrganizationStorePage<
        OrganizationMember,
        OrganizationMemberCursorPosition>?> ListOrganizationMembersAsync(
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var exists = await db.Organizations.AsNoTracking().AnyAsync(
            organization => organization.Id == organizationId.Value,
            cancellationToken);
        return exists
            ? await ListMembersCoreAsync(
                organizationId,
                null,
                after,
                limit,
                cancellationToken)
            : null;
    }

    private async Task<OrganizationStorePage<
        OrganizationMember,
        OrganizationMemberCursorPosition>> ListMembersCoreAsync(
            OrganizationId organizationId,
            UserId? authorizedUserId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var authorizedUserIdValue = authorizedUserId?.Value;
        var domains = await db.OrganizationAllowedEmailDomains.AsNoTracking()
            .Where(domain =>
                domain.OrganizationId == organizationId.Value &&
                (authorizedUserIdValue == null ||
                 db.OrganizationMembers.Any(actor =>
                     actor.OrganizationId == organizationId.Value &&
                     actor.UserId == authorizedUserIdValue)))
            .OrderBy(domain => domain.Domain)
            .Select(domain => domain.Domain)
            .ToArrayAsync(cancellationToken);
        var query =
            from membership in db.OrganizationMembers.AsNoTracking()
            join user in db.Users.AsNoTracking()
                on membership.UserId equals user.Id
            where membership.OrganizationId == organizationId.Value &&
                  (authorizedUserIdValue == null ||
                   db.OrganizationMembers.Any(actor =>
                       actor.OrganizationId == organizationId.Value &&
                       actor.UserId == authorizedUserIdValue))
            select new
            {
                membership.Id,
                membership.UserId,
                Name = user.DisplayName,
                Email = user.Email ?? string.Empty,
                user.ImageUrl,
                membership.Role,
                membership.JoinedAt
            };

        if (after is not null)
        {
            query = query.Where(row =>
                row.JoinedAt > after.JoinedAt ||
                row.JoinedAt == after.JoinedAt &&
                row.Id.CompareTo(after.Id.Value) > 0);
        }

        var rows = await query
            .OrderBy(row => row.JoinedAt)
            .ThenBy(row => row.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var pageRows = hasMore ? rows[..limit] : rows;
        var items = pageRows.Select(row =>
        {
            var eligibility = OrganizationEmailDomainPolicy.Evaluate(
                row.Email,
                domains);
            return new OrganizationMember(
                new OrganizationMemberId(row.Id),
                new UserId(row.UserId),
                row.Name,
                row.Email,
                row.ImageUrl,
                ParseRole(row.Role),
                row.JoinedAt,
                eligibility.EmailDomain,
                !eligibility.IsAllowed);
        }).ToArray();
        var next = hasMore
            ? new OrganizationMemberCursorPosition(
                pageRows[^1].JoinedAt,
                new OrganizationMemberId(pageRows[^1].Id))
            : null;
        return new(items, next);
    }

    private static MachineOrganizationSummary MapUserOrganization(
        Guid id,
        string name,
        string slug,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string roleValue)
    {
        var role = ParseRole(roleValue);
        return new(
            new OrganizationId(id),
            name,
            slug,
            createdAt,
            updatedAt,
            "user",
            role.Value,
            OrganizationPermissionPolicy.GetCapabilities(role));
    }

    private static OrganizationRole ParseRole(string value) =>
        OrganizationRole.TryParse(value, out var role)
            ? role
            : throw new InvalidOperationException(
                "The database contains an unknown organization role.");
}
