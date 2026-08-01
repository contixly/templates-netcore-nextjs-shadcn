using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Application.Common.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Collaboration;

internal sealed class EfTeamStore(
    TemplateDbContext db,
    IApplicationUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ITeamStore
{
    private const int EmbeddedMemberLimit = 50;
    private const int MaximumConcurrencyAttempts = 3;

    public async Task<
        TeamOperationResult<
            TeamStorePage<TeamStoreSummary, TeamCursorPosition>>> ListAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamCursorPosition? after,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSnapshotReadAsync(
                async transactionCancellationToken =>
                {
                    if (!await CanReadAsync(
                            actorUserId.Value,
                            organizationId.Value,
                            transactionCancellationToken))
                    {
                        return TeamOperationResult<
                            TeamStorePage<
                                TeamStoreSummary,
                                TeamCursorPosition>>.Failed(
                                    TeamFailure.NotFound);
                    }

                    var query = db.Teams.AsNoTracking()
                        .Where(team =>
                            team.OrganizationId == organizationId.Value);
                    if (after is not null)
                    {
                        query = query.Where(team =>
                            team.CreatedAt > after.CreatedAt ||
                            (team.CreatedAt == after.CreatedAt &&
                             team.Id.CompareTo(after.Id.Value) > 0));
                    }

                    var rows = await query
                        .OrderBy(team => team.CreatedAt)
                        .ThenBy(team => team.Id)
                        .Take(limit + 1)
                        .ToArrayAsync(transactionCancellationToken);
                    var hasMore = rows.Length > limit;
                    var pageRows = hasMore ? rows[..limit] : rows;
                    var embeddedMembers =
                        await ReadEmbeddedMemberPagesAsync(
                            organizationId.Value,
                            pageRows.Select(team => team.Id).ToArray(),
                            transactionCancellationToken);
                    var items = new List<TeamStoreSummary>(pageRows.Length);
                    foreach (var row in pageRows)
                    {
                        var embedded = embeddedMembers[row.Id];
                        items.Add(MapStoreSummary(
                            row,
                            embedded.MemberCount,
                            embedded.Members));
                    }

                    var next = hasMore
                        ? new TeamCursorPosition(
                            pageRows[^1].CreatedAt,
                            new TeamId(pageRows[^1].Id))
                        : null;
                    return TeamOperationResult<
                        TeamStorePage<
                            TeamStoreSummary,
                            TeamCursorPosition>>.Success(new(items, next));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return TeamOperationResult<
                TeamStorePage<
                    TeamStoreSummary,
                    TeamCursorPosition>>.Failed(
                        TeamFailure.ConcurrencyConflict);
        }
    }

    public async Task<TeamOperationResult<TeamSummary>> CreateAsync(
        CreateTeamCommand command,
        CancellationToken cancellationToken)
    {
        return await ExecuteMutationWithRetryAsync(
            async transactionCancellationToken =>
            {
                if (await LockOrganizationAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamSummary>.Failed(
                        TeamFailure.NotFound);
                }

                var actor = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.ActorUserId.Value,
                    transactionCancellationToken);
                var authorization = AuthorizeMutation<TeamSummary>(actor);
                if (authorization is not null)
                {
                    return authorization;
                }

                if (await TeamNameExistsAsync(
                        command.OrganizationId.Value,
                        command.Name.Value,
                        excludedTeamId: null,
                        transactionCancellationToken))
                {
                    return TeamOperationResult<TeamSummary>.Failed(
                        TeamFailure.NameConflict);
                }

                var now = timeProvider.GetUtcNow();
                var teamId = TeamId.New(now);
                db.Teams.Add(new TeamEntity
                {
                    Id = teamId.Value,
                    OrganizationId = command.OrganizationId.Value,
                    Name = command.Name.Value,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                return TeamOperationResult<TeamSummary>.Success(new(
                    teamId,
                    command.OrganizationId,
                    command.Name,
                    MemberCount: 0,
                    new TeamMemberPage([], null),
                    now,
                    now));
            },
            exception => IsUniqueViolation(
                exception,
                "ux_teams_organization_id_lower_name")
                    ? TeamFailure.NameConflict
                    : null,
            cancellationToken);
    }

    public async Task<TeamOperationResult<TeamSummary>> UpdateAsync(
        UpdateTeamCommand command,
        CancellationToken cancellationToken)
    {
        return await ExecuteMutationWithRetryAsync(
            async transactionCancellationToken =>
            {
                if (await LockOrganizationAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamSummary>.Failed(
                        TeamFailure.NotFound);
                }

                var actor = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.ActorUserId.Value,
                    transactionCancellationToken);
                var authorization = AuthorizeMutation<TeamSummary>(actor);
                if (authorization is not null)
                {
                    return authorization;
                }

                var team = await LockTeamAsync(
                    command.OrganizationId.Value,
                    command.TeamId.Value,
                    transactionCancellationToken);
                if (team is null)
                {
                    return TeamOperationResult<TeamSummary>.Failed(
                        TeamFailure.NotFound);
                }

                if (await TeamNameMatchesAsync(
                        command.OrganizationId.Value,
                        command.TeamId.Value,
                        command.Name.Value,
                        transactionCancellationToken))
                {
                    return TeamOperationResult<TeamSummary>.Failed(
                        TeamFailure.NameUnchanged);
                }

                if (await TeamNameExistsAsync(
                        command.OrganizationId.Value,
                        command.Name.Value,
                        command.TeamId.Value,
                        transactionCancellationToken))
                {
                    return TeamOperationResult<TeamSummary>.Failed(
                        TeamFailure.NameConflict);
                }

                var now = timeProvider.GetUtcNow();
                team.Name = command.Name.Value;
                team.UpdatedAt = now;
                var members = await ReadMemberPageAsync(
                    command.OrganizationId.Value,
                    command.TeamId.Value,
                    after: null,
                    EmbeddedMemberLimit,
                    transactionCancellationToken);
                var memberCount = await db.TeamMembers.AsNoTracking()
                    .CountAsync(
                        member =>
                            member.OrganizationId ==
                            command.OrganizationId.Value &&
                            member.TeamId == command.TeamId.Value,
                        transactionCancellationToken);
                return TeamOperationResult<TeamSummary>.Success(new(
                    command.TeamId,
                    command.OrganizationId,
                    command.Name,
                    memberCount,
                    MapMemberPage(members),
                    team.CreatedAt,
                    now));
            },
            exception => IsUniqueViolation(
                exception,
                "ux_teams_organization_id_lower_name")
                    ? TeamFailure.NameConflict
                    : null,
            cancellationToken);
    }

    public async Task<TeamOperationResult<TeamDeletion>> DeleteAsync(
        DeleteTeamCommand command,
        CancellationToken cancellationToken)
    {
        return await ExecuteMutationWithRetryAsync(
            async transactionCancellationToken =>
            {
                if (await LockOrganizationAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamDeletion>.Failed(
                        TeamFailure.NotFound);
                }

                var actor = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.ActorUserId.Value,
                    transactionCancellationToken);
                var authorization = AuthorizeMutation<TeamDeletion>(actor);
                if (authorization is not null)
                {
                    return authorization;
                }

                var team = await LockTeamAsync(
                    command.OrganizationId.Value,
                    command.TeamId.Value,
                    transactionCancellationToken);
                if (team is null)
                {
                    return TeamOperationResult<TeamDeletion>.Failed(
                        TeamFailure.NotFound);
                }

                await db.Invitations
                    .Where(invitation =>
                        invitation.OrganizationId ==
                        command.OrganizationId.Value &&
                        invitation.TeamId == command.TeamId.Value)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            invitation => invitation.TeamId,
                            (Guid?)null),
                        transactionCancellationToken);
                db.Teams.Remove(team);
                return TeamOperationResult<TeamDeletion>.Success(
                    new(command.TeamId));
            },
            classifyException: null,
            cancellationToken);
    }

    public async Task<
        TeamOperationResult<
            TeamStorePage<
                TeamMemberView,
                TeamMemberCursorPosition>>> ListMembersAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        TeamMemberCursorPosition? after,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSnapshotReadAsync(
                async transactionCancellationToken =>
                {
                    if (!await CanReadAsync(
                            actorUserId.Value,
                            organizationId.Value,
                            transactionCancellationToken) ||
                        !await TeamExistsAsync(
                            organizationId.Value,
                            teamId.Value,
                            transactionCancellationToken))
                    {
                        return TeamOperationResult<
                            TeamStorePage<
                                TeamMemberView,
                                TeamMemberCursorPosition>>.Failed(
                                    TeamFailure.NotFound);
                    }

                    var page = await ReadMemberPageAsync(
                        organizationId.Value,
                        teamId.Value,
                        after,
                        limit,
                        transactionCancellationToken);
                    return TeamOperationResult<
                        TeamStorePage<
                            TeamMemberView,
                            TeamMemberCursorPosition>>.Success(page);
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return TeamOperationResult<
                TeamStorePage<
                    TeamMemberView,
                    TeamMemberCursorPosition>>.Failed(
                        TeamFailure.ConcurrencyConflict);
        }
    }

    public async Task<TeamOperationResult<TeamMemberView>> AddMemberAsync(
        AddTeamMemberCommand command,
        CancellationToken cancellationToken)
    {
        return await ExecuteMutationWithRetryAsync(
            async transactionCancellationToken =>
            {
                if (await LockOrganizationAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamMemberView>.Failed(
                        TeamFailure.NotFound);
                }

                var actor = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.ActorUserId.Value,
                    transactionCancellationToken);
                var authorization = AuthorizeMutation<TeamMemberView>(actor);
                if (authorization is not null)
                {
                    return authorization;
                }

                if (await LockTeamAsync(
                        command.OrganizationId.Value,
                        command.TeamId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamMemberView>.Failed(
                        TeamFailure.NotFound);
                }

                var target = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.TargetUserId.Value,
                    transactionCancellationToken);
                if (target is null)
                {
                    return TeamOperationResult<TeamMemberView>.Failed(
                        TeamFailure.MemberNotFound);
                }

                if (await LockTeamMemberAsync(
                        command.OrganizationId.Value,
                        command.TeamId.Value,
                        target.Id,
                        transactionCancellationToken) is not null)
                {
                    return TeamOperationResult<TeamMemberView>.Failed(
                        TeamFailure.MemberAlreadyExists);
                }

                var user = await ReadUserAsync(
                    command.TargetUserId.Value,
                    transactionCancellationToken);
                if (user is null)
                {
                    return TeamOperationResult<TeamMemberView>.Failed(
                        TeamFailure.MemberNotFound);
                }

                var now = timeProvider.GetUtcNow();
                var memberId = TeamMemberId.New(now);
                db.TeamMembers.Add(new TeamMemberEntity
                {
                    Id = memberId.Value,
                    OrganizationId = command.OrganizationId.Value,
                    TeamId = command.TeamId.Value,
                    OrganizationMemberId = target.Id,
                    JoinedAt = now
                });
                return TeamOperationResult<TeamMemberView>.Success(new(
                    memberId,
                    command.TargetUserId,
                    user.Name,
                    user.Email,
                    user.ImageUrl,
                    ParseRole(target.Role),
                    target.JoinedAt,
                    now));
            },
            exception => IsUniqueViolation(
                exception,
                "ux_team_members_team_id_organization_member_id")
                    ? TeamFailure.MemberAlreadyExists
                    : null,
            cancellationToken);
    }

    public async Task<TeamOperationResult<TeamMemberRemoval>>
        RemoveMemberAsync(
            RemoveTeamMemberCommand command,
            CancellationToken cancellationToken)
    {
        return await ExecuteMutationWithRetryAsync(
            async transactionCancellationToken =>
            {
                if (await LockOrganizationAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamMemberRemoval>.Failed(
                        TeamFailure.NotFound);
                }

                var actor = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.ActorUserId.Value,
                    transactionCancellationToken);
                var authorization =
                    AuthorizeMutation<TeamMemberRemoval>(actor);
                if (authorization is not null)
                {
                    return authorization;
                }

                if (await LockTeamAsync(
                        command.OrganizationId.Value,
                        command.TeamId.Value,
                        transactionCancellationToken) is null)
                {
                    return TeamOperationResult<TeamMemberRemoval>.Failed(
                        TeamFailure.NotFound);
                }

                var target = await LockMembershipAsync(
                    command.OrganizationId.Value,
                    command.TargetUserId.Value,
                    transactionCancellationToken);
                if (target is null)
                {
                    return TeamOperationResult<TeamMemberRemoval>.Failed(
                        TeamFailure.MemberNotFound);
                }

                var teamMember = await LockTeamMemberAsync(
                    command.OrganizationId.Value,
                    command.TeamId.Value,
                    target.Id,
                    transactionCancellationToken);
                if (teamMember is null)
                {
                    return TeamOperationResult<TeamMemberRemoval>.Failed(
                        TeamFailure.MemberNotFound);
                }

                db.TeamMembers.Remove(teamMember);
                return TeamOperationResult<TeamMemberRemoval>.Success(new(
                    command.TeamId,
                    command.TargetUserId));
            },
            classifyException: null,
            cancellationToken);
    }

    public async Task<
        TeamOperationResult<
            TeamStorePage<
                TeamCandidate,
                TeamCandidateCursorPosition>>> ListCandidatesAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        TeamId teamId,
        string? query,
        TeamCandidateCursorPosition? after,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSnapshotReadAsync(
                async transactionCancellationToken =>
                {
                    var authorization = await AuthorizeCandidateReadAsync(
                        actorUserId.Value,
                        organizationId.Value,
                        transactionCancellationToken);
                    if (authorization is not null)
                    {
                        return TeamOperationResult<
                            TeamStorePage<
                                TeamCandidate,
                                TeamCandidateCursorPosition>>.Failed(
                                    authorization.Value);
                    }

                    if (!await TeamExistsAsync(
                            organizationId.Value,
                            teamId.Value,
                            transactionCancellationToken))
                    {
                        return TeamOperationResult<
                            TeamStorePage<
                                TeamCandidate,
                                TeamCandidateCursorPosition>>.Failed(
                                    TeamFailure.NotFound);
                    }

                    var candidates =
                        from membership in
                            db.OrganizationMembers.AsNoTracking()
                        join user in db.Users.AsNoTracking()
                            on membership.UserId equals user.Id
                        where membership.OrganizationId ==
                                  organizationId.Value &&
                              !db.TeamMembers.AsNoTracking().Any(member =>
                                  member.OrganizationId ==
                                  organizationId.Value &&
                                  member.TeamId == teamId.Value &&
                                  member.OrganizationMemberId == membership.Id)
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
                    var normalizedQuery = query?.Trim();
                    if (!string.IsNullOrEmpty(normalizedQuery))
                    {
                        var pattern = $"%{EscapeLikePattern(normalizedQuery)}%";
                        candidates = candidates.Where(candidate =>
                            EF.Functions.ILike(candidate.Name, pattern, @"\") ||
                            EF.Functions.ILike(candidate.Email, pattern, @"\"));
                    }

                    if (after is not null)
                    {
                        candidates = candidates.Where(candidate =>
                            candidate.JoinedAt > after.JoinedAt ||
                            (candidate.JoinedAt == after.JoinedAt &&
                             candidate.Id.CompareTo(after.Id.Value) > 0));
                    }

                    var rows = await candidates
                        .OrderBy(candidate => candidate.JoinedAt)
                        .ThenBy(candidate => candidate.Id)
                        .Take(limit + 1)
                        .ToArrayAsync(transactionCancellationToken);
                    var hasMore = rows.Length > limit;
                    var pageRows = hasMore ? rows[..limit] : rows;
                    var items = pageRows.Select(candidate => MapCandidate(new(
                        candidate.Id,
                        candidate.UserId,
                        candidate.Name,
                        candidate.Email,
                        candidate.ImageUrl,
                        candidate.Role,
                        candidate.JoinedAt))).ToArray();
                    var next = hasMore
                        ? new TeamCandidateCursorPosition(
                            pageRows[^1].JoinedAt,
                            new OrganizationMemberId(pageRows[^1].Id))
                        : null;
                    return TeamOperationResult<
                        TeamStorePage<
                            TeamCandidate,
                            TeamCandidateCursorPosition>>.Success(
                                new(items, next));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return TeamOperationResult<
                TeamStorePage<
                    TeamCandidate,
                    TeamCandidateCursorPosition>>.Failed(
                        TeamFailure.ConcurrencyConflict);
        }
    }

    private async Task<TeamStorePage<TeamMemberView, TeamMemberCursorPosition>>
        ReadMemberPageAsync(
            Guid organizationId,
            Guid teamId,
            TeamMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
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
            where teamMember.OrganizationId == organizationId &&
                  teamMember.TeamId == teamId
            select new
            {
                teamMember.Id,
                organizationMember.UserId,
                Name = user.DisplayName,
                Email = user.Email ?? string.Empty,
                user.ImageUrl,
                organizationMember.Role,
                OrganizationJoinedAt = organizationMember.JoinedAt,
                TeamJoinedAt = teamMember.JoinedAt
            };
        if (after is not null)
        {
            query = query.Where(member =>
                member.TeamJoinedAt > after.JoinedAt ||
                (member.TeamJoinedAt == after.JoinedAt &&
                 member.Id.CompareTo(after.Id.Value) > 0));
        }

        var rows = await query
            .OrderBy(member => member.TeamJoinedAt)
            .ThenBy(member => member.Id)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var pageRows = hasMore ? rows[..limit] : rows;
        var items = pageRows.Select(member => MapMember(new(
            member.Id,
            member.UserId,
            member.Name,
            member.Email,
            member.ImageUrl,
            member.Role,
            member.OrganizationJoinedAt,
            member.TeamJoinedAt))).ToArray();
        var next = hasMore
            ? new TeamMemberCursorPosition(
                pageRows[^1].TeamJoinedAt,
                new TeamMemberId(pageRows[^1].Id))
            : null;
        return new(items, next);
    }

    private async Task<IReadOnlyDictionary<Guid, EmbeddedMemberProjection>>
        ReadEmbeddedMemberPagesAsync(
            Guid organizationId,
            Guid[] teamIds,
            CancellationToken cancellationToken)
    {
        var pages = teamIds.ToDictionary(
            teamId => teamId,
            _ => new EmbeddedMemberProjection(
                MemberCount: 0,
                new TeamStorePage<
                    TeamMemberView,
                    TeamMemberCursorPosition>([], null)));
        if (teamIds.Length == 0)
        {
            return pages;
        }

        var maximumRowNumber = EmbeddedMemberLimit + 1;
        var rows = await db.Database.SqlQuery<EmbeddedMemberReadRow>(
                $"""
                 SELECT ranked.team_id AS "TeamId",
                        ranked.id AS "Id",
                        ranked.user_id AS "UserId",
                        ranked.name AS "Name",
                        ranked.email AS "Email",
                        ranked.image_url AS "ImageUrl",
                        ranked.role AS "Role",
                        ranked.organization_joined_at AS "OrganizationJoinedAt",
                        ranked.team_joined_at AS "TeamJoinedAt",
                        ranked.member_count AS "MemberCount",
                        ranked.row_number AS "RowNumber"
                 FROM (
                     SELECT team_member.team_id,
                            team_member.id,
                            organization_member.user_id,
                            "user".display_name AS name,
                            COALESCE("user".email, '') AS email,
                            "user".image_url,
                            organization_member.role,
                            organization_member.joined_at AS organization_joined_at,
                            team_member.joined_at AS team_joined_at,
                            CAST(COUNT(*) OVER (
                                PARTITION BY team_member.team_id) AS integer)
                                AS member_count,
                            CAST(ROW_NUMBER() OVER (
                                PARTITION BY team_member.team_id
                                ORDER BY team_member.joined_at, team_member.id)
                                AS integer) AS row_number
                     FROM organizations.team_members AS team_member
                     INNER JOIN organizations.members AS organization_member
                         ON organization_member.organization_id =
                            team_member.organization_id
                        AND organization_member.id =
                            team_member.organization_member_id
                     INNER JOIN auth.users AS "user"
                         ON "user".id = organization_member.user_id
                     WHERE team_member.organization_id = {organizationId}
                       AND team_member.team_id = ANY ({teamIds})
                 ) AS ranked
                 WHERE ranked.row_number <= {maximumRowNumber}
                 """)
            .ToArrayAsync(cancellationToken);

        foreach (var group in rows.GroupBy(row => row.TeamId))
        {
            var orderedRows = group
                .OrderBy(row => row.TeamJoinedAt)
                .ThenBy(row => row.Id)
                .ToArray();
            var memberCount = orderedRows[0].MemberCount;
            var pageRows = orderedRows.Take(EmbeddedMemberLimit).ToArray();
            var members = pageRows.Select(row => MapMember(new(
                row.Id,
                row.UserId,
                row.Name,
                row.Email,
                row.ImageUrl,
                row.Role,
                row.OrganizationJoinedAt,
                row.TeamJoinedAt))).ToArray();
            var next = memberCount > EmbeddedMemberLimit
                ? new TeamMemberCursorPosition(
                    pageRows[^1].TeamJoinedAt,
                    new TeamMemberId(pageRows[^1].Id))
                : null;
            pages[group.Key] = new EmbeddedMemberProjection(
                memberCount,
                new TeamStorePage<
                    TeamMemberView,
                    TeamMemberCursorPosition>(members, next));
        }

        return pages;
    }

    private async Task<T> ExecuteSnapshotReadAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            return await action(cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the read failure when rollback cannot complete.
            }

            throw;
        }
    }

    private async Task<TeamOperationResult<T>> ExecuteMutationWithRetryAsync<T>(
        Func<CancellationToken, Task<TeamOperationResult<T>>> action,
        Func<Exception, TeamFailure?>? classifyException,
        CancellationToken cancellationToken)
        where T : class
    {
        for (var attempt = 1; attempt <= MaximumConcurrencyAttempts; attempt++)
        {
            try
            {
                return await unitOfWork.ExecuteAsync(action, cancellationToken);
            }
            catch (Exception exception)
            {
                var classified = classifyException?.Invoke(exception);
                if (classified is not null)
                {
                    return TeamOperationResult<T>.Failed(classified.Value);
                }

                if (!IsConcurrencyFailure(exception))
                {
                    throw;
                }

                if (attempt == MaximumConcurrencyAttempts)
                {
                    return TeamOperationResult<T>.Failed(
                        TeamFailure.ConcurrencyConflict);
                }
            }
        }

        return TeamOperationResult<T>.Failed(TeamFailure.ConcurrencyConflict);
    }

    private Task<bool> CanReadAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        db.OrganizationMembers.AsNoTracking().AnyAsync(
            membership =>
                membership.OrganizationId == organizationId &&
                membership.UserId == actorUserId,
            cancellationToken);

    private Task<bool> TeamExistsAsync(
        Guid organizationId,
        Guid teamId,
        CancellationToken cancellationToken) =>
        db.Teams.AsNoTracking().AnyAsync(
            team =>
                team.OrganizationId == organizationId &&
                team.Id == teamId,
            cancellationToken);

    private Task<bool> TeamNameExistsAsync(
        Guid organizationId,
        string name,
        Guid? excludedTeamId,
        CancellationToken cancellationToken)
    {
        return excludedTeamId is null
            ? db.Database.SqlQuery<bool>(
                    $"""
                     SELECT EXISTS (
                         SELECT 1
                         FROM organizations.teams AS team
                         WHERE team.organization_id = {organizationId}
                           AND lower(team.name) = lower({name})
                     ) AS "Value"
                     """)
                .SingleAsync(cancellationToken)
            : db.Database.SqlQuery<bool>(
                    $"""
                     SELECT EXISTS (
                         SELECT 1
                         FROM organizations.teams AS team
                         WHERE team.organization_id = {organizationId}
                           AND team.id <> {excludedTeamId.Value}
                           AND lower(team.name) = lower({name})
                     ) AS "Value"
                     """)
                .SingleAsync(cancellationToken);
    }

    private Task<bool> TeamNameMatchesAsync(
        Guid organizationId,
        Guid teamId,
        string name,
        CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>(
                $"""
                 SELECT EXISTS (
                     SELECT 1
                     FROM organizations.teams AS team
                     WHERE team.organization_id = {organizationId}
                       AND team.id = {teamId}
                       AND lower(team.name) = lower({name})
                 ) AS "Value"
                 """)
            .SingleAsync(cancellationToken);

    private async Task<TeamFailure?> AuthorizeCandidateReadAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var roleValue = await db.OrganizationMembers.AsNoTracking()
            .Where(membership =>
                membership.OrganizationId == organizationId &&
                membership.UserId == actorUserId)
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (roleValue is null)
        {
            return TeamFailure.NotFound;
        }

        return OrganizationPermissionPolicy
            .GetCapabilities(ParseRole(roleValue))
            .CanManageTeams
                ? null
                : TeamFailure.PermissionDenied;
    }

    private static string EscapeLikePattern(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private Task<OrganizationEntity?> LockOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        db.Organizations
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.organizations
                 WHERE id = {organizationId}
                 FOR UPDATE
                 """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    private Task<OrganizationMemberEntity?> LockMembershipAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.members
                 WHERE organization_id = {organizationId}
                   AND user_id = {userId}
                 FOR UPDATE
                 """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    private Task<TeamEntity?> LockTeamAsync(
        Guid organizationId,
        Guid teamId,
        CancellationToken cancellationToken) =>
        db.Teams
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.teams
                 WHERE organization_id = {organizationId}
                   AND id = {teamId}
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<TeamMemberEntity?> LockTeamMemberAsync(
        Guid organizationId,
        Guid teamId,
        Guid organizationMemberId,
        CancellationToken cancellationToken) =>
        db.TeamMembers
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.team_members
                 WHERE organization_id = {organizationId}
                   AND team_id = {teamId}
                   AND organization_member_id = {organizationMemberId}
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<UserReadRow?> ReadUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserReadRow(
                user.DisplayName,
                user.Email ?? string.Empty,
                user.ImageUrl))
            .SingleOrDefaultAsync(cancellationToken);

    private static TeamOperationResult<T>? AuthorizeMutation<T>(
        OrganizationMemberEntity? actor)
        where T : class
    {
        if (actor is null)
        {
            return TeamOperationResult<T>.Failed(TeamFailure.NotFound);
        }

        var role = ParseRole(actor.Role);
        return OrganizationPermissionPolicy.GetCapabilities(role).CanManageTeams
            ? null
            : TeamOperationResult<T>.Failed(TeamFailure.PermissionDenied);
    }

    private static TeamStoreSummary MapStoreSummary(
        TeamEntity row,
        int memberCount,
        TeamStorePage<TeamMemberView, TeamMemberCursorPosition> members)
    {
        if (!TeamName.TryCreate(row.Name, out var name))
        {
            throw new InvalidOperationException(
                "The database contains an invalid team name.");
        }

        return new TeamStoreSummary(
            new TeamId(row.Id),
            new OrganizationId(row.OrganizationId),
            name,
            memberCount,
            members,
            row.CreatedAt,
            row.UpdatedAt);
    }

    private static TeamMemberPage MapMemberPage(
        TeamStorePage<TeamMemberView, TeamMemberCursorPosition> members) =>
        new(
            members.Items,
            members.Next is null ? null : TeamCursor.Encode(members.Next));

    private static TeamMemberView MapMember(MemberReadRow row) => new(
        new TeamMemberId(row.Id),
        new UserId(row.UserId),
        row.Name,
        row.Email,
        row.ImageUrl,
        ParseRole(row.Role),
        row.OrganizationJoinedAt,
        row.TeamJoinedAt);

    private static TeamCandidate MapCandidate(CandidateReadRow row) => new(
        new OrganizationMemberId(row.Id),
        new UserId(row.UserId),
        row.Name,
        row.Email,
        row.ImageUrl,
        ParseRole(row.Role),
        row.JoinedAt);

    private static OrganizationRole ParseRole(string value) =>
        OrganizationRole.TryParse(value, out var role)
            ? role
            : throw new InvalidOperationException(
                "The database contains an unknown organization role.");

    private static bool IsUniqueViolation(
        Exception exception,
        string constraintName)
    {
        var postgres = FindPostgresException(exception);
        return postgres?.SqlState == PostgresErrorCodes.UniqueViolation &&
               string.Equals(
                   postgres.ConstraintName,
                   constraintName,
                   StringComparison.Ordinal);
    }

    private static bool IsConcurrencyFailure(Exception exception)
    {
        var postgres = FindPostgresException(exception);
        return postgres?.SqlState is
            PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected;
    }

    private static PostgresException? FindPostgresException(
        Exception exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }

        return null;
    }

    private sealed record MemberReadRow(
        Guid Id,
        Guid UserId,
        string Name,
        string Email,
        string? ImageUrl,
        string Role,
        DateTimeOffset OrganizationJoinedAt,
        DateTimeOffset TeamJoinedAt);

    private sealed record CandidateReadRow(
        Guid Id,
        Guid UserId,
        string Name,
        string Email,
        string? ImageUrl,
        string Role,
        DateTimeOffset JoinedAt);

    private sealed record UserReadRow(
        string Name,
        string Email,
        string? ImageUrl);

    private sealed record EmbeddedMemberProjection(
        int MemberCount,
        TeamStorePage<TeamMemberView, TeamMemberCursorPosition> Members);

    private sealed class EmbeddedMemberReadRow
    {
        public Guid TeamId { get; set; }
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public required string Name { get; set; }
        public required string Email { get; set; }
        public string? ImageUrl { get; set; }
        public required string Role { get; set; }
        public DateTimeOffset OrganizationJoinedAt { get; set; }
        public DateTimeOffset TeamJoinedAt { get; set; }
        public int MemberCount { get; set; }
        public int RowNumber { get; set; }
    }
}
