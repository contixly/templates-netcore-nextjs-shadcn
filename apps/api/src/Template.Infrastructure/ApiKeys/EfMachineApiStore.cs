using Microsoft.EntityFrameworkCore;
using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Application.Collaboration;
using Template.Application.Organizations;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.ApiKeys;

internal sealed class EfMachineApiStore(TemplateDbContext db)
    : IMachineApiStore
{
    private const int EmbeddedMemberLimit = 50;

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

    public async Task<TeamStorePage<
        MachineTeamStoreSummary,
        TeamCursorPosition>?> ListUserOrganizationTeamsAsync(
            UserId userId,
            OrganizationId organizationId,
            TeamCursorPosition? after,
            int limit,
            bool includeMembers,
            CancellationToken cancellationToken)
    {
        var accessible = await db.OrganizationMembers.AsNoTracking().AnyAsync(
            membership =>
                membership.OrganizationId == organizationId.Value &&
                membership.UserId == userId.Value,
            cancellationToken);
        return accessible
            ? await ListTeamsCoreAsync(
                organizationId,
                userId,
                after,
                limit,
                includeMembers,
                cancellationToken)
            : null;
    }

    public async Task<TeamStorePage<
        MachineTeamStoreSummary,
        TeamCursorPosition>?> ListOrganizationTeamsAsync(
            OrganizationId organizationId,
            TeamCursorPosition? after,
            int limit,
            bool includeMembers,
            CancellationToken cancellationToken)
    {
        var exists = await db.Organizations.AsNoTracking().AnyAsync(
            organization => organization.Id == organizationId.Value,
            cancellationToken);
        return exists
            ? await ListTeamsCoreAsync(
                organizationId,
                authorizedUserId: null,
                after,
                limit,
                includeMembers,
                cancellationToken)
            : null;
    }

    public async Task<TeamStorePage<
        TeamMemberView,
        TeamMemberCursorPosition>?> ListUserOrganizationTeamMembersAsync(
            UserId userId,
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var accessibleTeam = await db.Teams.AsNoTracking().AnyAsync(
            team =>
                team.OrganizationId == organizationId.Value &&
                team.Id == teamId.Value &&
                db.OrganizationMembers.Any(membership =>
                    membership.OrganizationId == organizationId.Value &&
                    membership.UserId == userId.Value),
            cancellationToken);
        return accessibleTeam
            ? await ListTeamMembersCoreAsync(
                organizationId,
                teamId,
                userId,
                after,
                limit,
                cancellationToken)
            : null;
    }

    public async Task<TeamStorePage<
        TeamMemberView,
        TeamMemberCursorPosition>?> ListOrganizationTeamMembersAsync(
            OrganizationId organizationId,
            TeamId teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var exists = await db.Teams.AsNoTracking().AnyAsync(
            team =>
                team.OrganizationId == organizationId.Value &&
                team.Id == teamId.Value,
            cancellationToken);
        return exists
            ? await ListTeamMembersCoreAsync(
                organizationId,
                teamId,
                authorizedUserId: null,
                after,
                limit,
                cancellationToken)
            : null;
    }

    private async Task<TeamStorePage<
        MachineTeamStoreSummary,
        TeamCursorPosition>> ListTeamsCoreAsync(
            OrganizationId organizationId,
            UserId? authorizedUserId,
            TeamCursorPosition? after,
            int limit,
            bool includeMembers,
            CancellationToken cancellationToken)
    {
        var authorizedUserIdValue = authorizedUserId?.Value;
        var query = db.Teams.AsNoTracking()
            .Where(team =>
                team.OrganizationId == organizationId.Value &&
                (authorizedUserIdValue == null ||
                 db.OrganizationMembers.Any(membership =>
                     membership.OrganizationId == organizationId.Value &&
                     membership.UserId == authorizedUserIdValue)));
        if (after is not null)
        {
            query = query.Where(team =>
                team.CreatedAt > after.CreatedAt ||
                team.CreatedAt == after.CreatedAt &&
                team.Id.CompareTo(after.Id.Value) > 0);
        }

        var rows = await query
            .OrderBy(team => team.CreatedAt)
            .ThenBy(team => team.Id)
            .Select(team => new TeamReadRow
            {
                Id = team.Id,
                OrganizationId = team.OrganizationId,
                Name = team.Name,
                MemberCount = db.TeamMembers.Count(member =>
                    member.OrganizationId == organizationId.Value &&
                    member.TeamId == team.Id),
                CreatedAt = team.CreatedAt,
                UpdatedAt = team.UpdatedAt
            })
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var pageRows = hasMore ? rows[..limit] : rows;
        var embedded = includeMembers
            ? await ReadEmbeddedMemberPagesAsync(
                organizationId,
                authorizedUserId,
                pageRows.Select(team => team.Id).ToArray(),
                cancellationToken)
            : pageRows.ToDictionary(
                team => team.Id,
                _ => new TeamStorePage<
                    TeamMemberView,
                    TeamMemberCursorPosition>([], null));
        var items = pageRows.Select(team => new MachineTeamStoreSummary(
            new TeamId(team.Id),
            new OrganizationId(team.OrganizationId),
            ParseTeamName(team.Name),
            team.MemberCount,
            embedded[team.Id],
            team.CreatedAt,
            team.UpdatedAt,
            includeMembers)).ToArray();
        var next = hasMore
            ? new TeamCursorPosition(
                pageRows[^1].CreatedAt,
                new TeamId(pageRows[^1].Id))
            : null;
        return new(items, next);
    }

    private async Task<IReadOnlyDictionary<
        Guid,
        TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>>
        ReadEmbeddedMemberPagesAsync(
            OrganizationId organizationId,
            UserId? authorizedUserId,
            Guid[] teamIds,
            CancellationToken cancellationToken)
    {
        var pages = teamIds.ToDictionary(
            teamId => teamId,
            _ => new TeamStorePage<
                TeamMemberView,
                TeamMemberCursorPosition>([], null));
        if (teamIds.Length == 0)
        {
            return pages;
        }

        var authorizedUserIdValue = authorizedUserId?.Value;
        var maximumRows = EmbeddedMemberLimit + 1;
        var rows = await db.Teams.AsNoTracking()
            .Where(team =>
                team.OrganizationId == organizationId.Value &&
                teamIds.Contains(team.Id) &&
                (authorizedUserIdValue == null ||
                 db.OrganizationMembers.Any(actor =>
                     actor.OrganizationId == organizationId.Value &&
                     actor.UserId == authorizedUserIdValue)))
            .Select(team => new
            {
                TeamId = team.Id,
                Members = (
                    from teamMember in db.TeamMembers.AsNoTracking()
                    join organizationMember in
                        db.OrganizationMembers.AsNoTracking()
                        on new
                        {
                            teamMember.OrganizationId,
                            Id = teamMember.OrganizationMemberId
                        }
                        equals new
                        {
                            organizationMember.OrganizationId,
                            organizationMember.Id
                        }
                    join user in db.Users.AsNoTracking()
                        on organizationMember.UserId equals user.Id
                    where teamMember.OrganizationId == organizationId.Value &&
                          teamMember.TeamId == team.Id &&
                          (authorizedUserIdValue == null ||
                           db.OrganizationMembers.Any(actor =>
                               actor.OrganizationId == organizationId.Value &&
                               actor.UserId == authorizedUserIdValue))
                    orderby teamMember.JoinedAt, teamMember.Id
                    select new MemberReadRow
                    {
                        Id = teamMember.Id,
                        UserId = organizationMember.UserId,
                        Name = user.DisplayName,
                        Email = user.Email ?? string.Empty,
                        ImageUrl = user.ImageUrl,
                        Role = organizationMember.Role,
                        OrganizationJoinedAt = organizationMember.JoinedAt,
                        TeamJoinedAt = teamMember.JoinedAt
                    })
                    .Take(maximumRows)
                    .ToArray()
            })
            .ToArrayAsync(cancellationToken);

        foreach (var row in rows)
        {
            var hasMore = row.Members.Length > EmbeddedMemberLimit;
            var pageRows = hasMore
                ? row.Members[..EmbeddedMemberLimit]
                : row.Members;
            var items = pageRows.Select(MapMember).ToArray();
            var next = hasMore
                ? new TeamMemberCursorPosition(
                    pageRows[^1].TeamJoinedAt,
                    new TeamMemberId(pageRows[^1].Id))
                : null;
            pages[row.TeamId] = new(items, next);
        }

        return pages;
    }

    private async Task<TeamStorePage<
        TeamMemberView,
        TeamMemberCursorPosition>> ListTeamMembersCoreAsync(
            OrganizationId organizationId,
            TeamId teamId,
            UserId? authorizedUserId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var authorizedUserIdValue = authorizedUserId?.Value;
        var query =
            from teamMember in db.TeamMembers.AsNoTracking()
            join organizationMember in db.OrganizationMembers.AsNoTracking()
                on new
                {
                    teamMember.OrganizationId,
                    Id = teamMember.OrganizationMemberId
                }
                equals new
                {
                    organizationMember.OrganizationId,
                    organizationMember.Id
                }
            join user in db.Users.AsNoTracking()
                on organizationMember.UserId equals user.Id
            where teamMember.OrganizationId == organizationId.Value &&
                  teamMember.TeamId == teamId.Value &&
                  (authorizedUserIdValue == null ||
                   db.OrganizationMembers.Any(actor =>
                       actor.OrganizationId == organizationId.Value &&
                       actor.UserId == authorizedUserIdValue))
            select new MemberReadRow
            {
                Id = teamMember.Id,
                UserId = organizationMember.UserId,
                Name = user.DisplayName,
                Email = user.Email ?? string.Empty,
                ImageUrl = user.ImageUrl,
                Role = organizationMember.Role,
                OrganizationJoinedAt = organizationMember.JoinedAt,
                TeamJoinedAt = teamMember.JoinedAt
            };
        if (after is not null)
        {
            query = query.Where(member =>
                member.TeamJoinedAt > after.JoinedAt ||
                member.TeamJoinedAt == after.JoinedAt &&
                member.Id.CompareTo(after.Id.Value) > 0);
        }

        var rows = await query
            .OrderBy(member => member.TeamJoinedAt)
            .ThenBy(member => member.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var pageRows = hasMore ? rows[..limit] : rows;
        var items = pageRows.Select(MapMember).ToArray();
        var next = hasMore
            ? new TeamMemberCursorPosition(
                pageRows[^1].TeamJoinedAt,
                new TeamMemberId(pageRows[^1].Id))
            : null;
        return new(items, next);
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

    private static TeamName ParseTeamName(string value) =>
        Template.Domain.Collaboration.TeamName.TryCreate(value, out var name)
            ? name
            : throw new InvalidOperationException(
                "The database contains an invalid team name.");

    private static TeamMemberView MapMember(MemberReadRow row) => new(
        new TeamMemberId(row.Id),
        new UserId(row.UserId),
        row.Name,
        row.Email,
        row.ImageUrl,
        ParseRole(row.Role),
        row.OrganizationJoinedAt,
        row.TeamJoinedAt);

    private static OrganizationRole ParseRole(string value) =>
        OrganizationRole.TryParse(value, out var role)
            ? role
            : throw new InvalidOperationException(
                "The database contains an unknown organization role.");

    private sealed class TeamReadRow
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public required string Name { get; set; }
        public int MemberCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class MemberReadRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public required string Name { get; set; }
        public required string Email { get; set; }
        public string? ImageUrl { get; set; }
        public required string Role { get; set; }
        public DateTimeOffset OrganizationJoinedAt { get; set; }
        public DateTimeOffset TeamJoinedAt { get; set; }
    }
}
