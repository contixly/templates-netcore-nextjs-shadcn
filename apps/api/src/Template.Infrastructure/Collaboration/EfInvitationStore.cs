using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Application.Common.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Collaboration;

internal sealed class EfInvitationStore(
    TemplateDbContext db,
    IApplicationUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IInvitationStore
{
    private const int MaximumPendingPerInviter = 100;
    private const int MaximumConcurrencyAttempts = 3;
    private const int OrganizationNameAdvisoryLockNamespace = 1_330_792_270;

    public async Task<InvitationOperationResult<
        InvitationStorePage<InvitationView, OrganizationInvitationCursorPosition>>>
        ListOrganizationAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            InvitationDisplayState? filter,
            OrganizationInvitationCursorPosition? after,
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSnapshotReadAsync(
                async transactionCancellationToken =>
                {
                    var authorization = await AuthorizeOrganizationReadAsync(
                        actorUserId.Value,
                        organizationId.Value,
                        transactionCancellationToken);
                    if (authorization is not null)
                    {
                        return InvitationOperationResult<
                            InvitationStorePage<
                                InvitationView,
                                OrganizationInvitationCursorPosition>>.Failed(
                                    authorization.Value);
                    }

                    var query = db.Invitations.AsNoTracking()
                        .Where(row => row.OrganizationId == organizationId.Value);
                    query = ApplyDisplayFilter(query, filter, now);
                    if (after is not null)
                    {
                        query = query.Where(row =>
                            row.CreatedAt < after.CreatedAt ||
                            (row.CreatedAt == after.CreatedAt &&
                             row.Id.CompareTo(after.Id.Value) < 0));
                    }

                    var rows = await query
                        .OrderByDescending(row => row.CreatedAt)
                        .ThenByDescending(row => row.Id)
                        .Take(limit + 1)
                        .ToArrayAsync(transactionCancellationToken);
                    var hasMore = rows.Length > limit;
                    var pageRows = hasMore ? rows[..limit] : rows;
                    var items = await ReadViewsAsync(
                        pageRows.Select(row => row.Id).ToArray(),
                        now,
                        transactionCancellationToken);
                    var next = hasMore
                        ? new OrganizationInvitationCursorPosition(
                            pageRows[^1].CreatedAt,
                            new InvitationId(pageRows[^1].Id))
                        : null;
                    return InvitationOperationResult<
                        InvitationStorePage<
                            InvitationView,
                            OrganizationInvitationCursorPosition>>.Success(
                                new(items, next));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return InvitationOperationResult<
                InvitationStorePage<
                    InvitationView,
                    OrganizationInvitationCursorPosition>>.Failed(
                        InvitationFailure.ConcurrencyConflict);
        }
    }

    public async Task<InvitationOperationResult<
        InvitationStorePage<InvitationView, AccountInvitationCursorPosition>>>
        ListAccountAsync(
            InvitationActor actor,
            AccountInvitationCursorPosition? after,
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSnapshotReadAsync(
                async transactionCancellationToken =>
                {
                    var email = NormalizeEmail(actor.NormalizedPrimaryEmail);
                    var query = db.Invitations.AsNoTracking()
                        .Where(invitation =>
                            invitation.Email == email &&
                            invitation.Status == InvitationStatus.Pending.Value &&
                            invitation.ExpiresAt > now &&
                            !db.OrganizationMembers.AsNoTracking().Any(membership =>
                                membership.OrganizationId ==
                                    invitation.OrganizationId &&
                                membership.UserId == actor.UserId.Value));
                    if (after is not null)
                    {
                        query = query.Where(row =>
                            row.ExpiresAt > after.ExpiresAt ||
                            (row.ExpiresAt == after.ExpiresAt &&
                             (row.CreatedAt < after.CreatedAt ||
                              (row.CreatedAt == after.CreatedAt &&
                               row.Id.CompareTo(after.Id.Value) < 0))));
                    }

                    var rows = await query
                        .OrderBy(row => row.ExpiresAt)
                        .ThenByDescending(row => row.CreatedAt)
                        .ThenByDescending(row => row.Id)
                        .Take(limit + 1)
                        .ToArrayAsync(transactionCancellationToken);
                    var hasMore = rows.Length > limit;
                    var pageRows = hasMore ? rows[..limit] : rows;
                    var items = await ReadViewsAsync(
                        pageRows.Select(row => row.Id).ToArray(),
                        now,
                        transactionCancellationToken);
                    var next = hasMore
                        ? new AccountInvitationCursorPosition(
                            pageRows[^1].ExpiresAt,
                            pageRows[^1].CreatedAt,
                            new InvitationId(pageRows[^1].Id))
                        : null;
                    return InvitationOperationResult<
                        InvitationStorePage<
                            InvitationView,
                            AccountInvitationCursorPosition>>.Success(
                                new(items, next));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return InvitationOperationResult<
                InvitationStorePage<
                    InvitationView,
                    AccountInvitationCursorPosition>>.Failed(
                        InvitationFailure.ConcurrencyConflict);
        }
    }

    public async Task<InvitationOperationResult<InvitationDecision>>
        GetDecisionAsync(
            InvitationActor actor,
            InvitationId invitationId,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSnapshotReadAsync(
                async transactionCancellationToken =>
                {
                    var invitation = await db.Invitations.AsNoTracking()
                        .SingleOrDefaultAsync(
                            row => row.Id == invitationId.Value,
                            transactionCancellationToken);
                    if (invitation is null)
                    {
                        return InvitationOperationResult<
                            InvitationDecision>.Failed(
                                InvitationFailure.NotFound);
                    }

                    if (!RecipientMatches(actor, invitation.Email))
                    {
                        return InvitationOperationResult<
                            InvitationDecision>.Failed(
                                InvitationFailure.RecipientMismatch);
                    }

                    var view = await ReadViewAsync(
                        invitation.Id,
                        now,
                        transactionCancellationToken);
                    var state = await ClassifyDecisionAsync(
                        actor,
                        invitation,
                        view,
                        transactionCancellationToken);
                    return InvitationOperationResult<InvitationDecision>.Success(
                        new(view, state, state == InvitationDecisionState.Pending));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return InvitationOperationResult<InvitationDecision>.Failed(
                InvitationFailure.ConcurrencyConflict);
        }
    }

    public async Task<InvitationOperationResult<InvitationView>> CreateAsync(
        CreateInvitationCommand command,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    var organization = await LockOrganizationAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken);
                    if (organization is null)
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.NotFound);
                    }

                    var actor = await LockMembershipAsync(
                        command.OrganizationId.Value,
                        command.ActorUserId.Value,
                        transactionCancellationToken);
                    if (actor is null)
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.NotFound);
                    }

                    var actorRole = ParseRole(actor.Role);
                    if (!OrganizationPermissionPolicy
                            .GetCapabilities(actorRole)
                            .CanManageInvitations ||
                        !OrganizationPermissionPolicy.CanAssign(
                            actorRole,
                            command.Role))
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.PermissionDenied);
                    }

                    TeamEntity? team = null;
                    if (command.TeamId is not null)
                    {
                        team = await LockTeamAsync(
                            command.OrganizationId.Value,
                            command.TeamId.Value.Value,
                            transactionCancellationToken);
                        if (team is null)
                        {
                            return InvitationOperationResult<InvitationView>.Failed(
                                InvitationFailure.TeamInvalid);
                        }
                    }

                    var email = NormalizeEmail(command.Email);
                    var domains = await ReadAllowedDomainsAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken);
                    if (!OrganizationEmailDomainPolicy.IsAllowed(email, domains))
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.DomainRestricted);
                    }

                    if (await IsCurrentMemberByEmailAsync(
                            command.OrganizationId.Value,
                            email,
                            transactionCancellationToken))
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.RecipientAlreadyMember);
                    }

                    var now = timeProvider.GetUtcNow();
                    var duplicate = await LockPendingDuplicateAsync(
                        command.OrganizationId.Value,
                        email,
                        transactionCancellationToken);
                    if (duplicate is not null && duplicate.ExpiresAt > now)
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.AlreadyExists);
                    }

                    var pendingCount = await db.Invitations.AsNoTracking()
                        .CountAsync(
                            row =>
                                row.OrganizationId == command.OrganizationId.Value &&
                                row.InviterUserId == command.ActorUserId.Value &&
                                row.Status == InvitationStatus.Pending.Value &&
                                row.ExpiresAt > now,
                            transactionCancellationToken);
                    if (pendingCount >= MaximumPendingPerInviter)
                    {
                        return InvitationOperationResult<InvitationView>.Failed(
                            InvitationFailure.LimitReached);
                    }

                    if (duplicate is not null)
                    {
                        duplicate.Status = InvitationStatus.Canceled.Value;
                        duplicate.UpdatedAt = now;
                    }

                    var inviterName = await db.Users.AsNoTracking()
                        .Where(user => user.Id == command.ActorUserId.Value)
                        .Select(user => user.DisplayName)
                        .SingleAsync(transactionCancellationToken);
                    var invitation = new InvitationEntity
                    {
                        Id = InvitationId.New().Value,
                        OrganizationId = command.OrganizationId.Value,
                        TeamId = command.TeamId?.Value,
                        Email = email,
                        Role = command.Role.Value,
                        Status = InvitationStatus.Pending.Value,
                        InviterUserId = command.ActorUserId.Value,
                        ExpiresAt = expiresAt,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.Invitations.Add(invitation);
                    return InvitationOperationResult<InvitationView>.Success(
                        MapView(
                            invitation,
                            organization.Name,
                            organization.Slug,
                            team?.Name,
                            inviterName,
                            now));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsUniqueViolation(
            exception,
            "ux_invitations_organization_id_email_pending"))
        {
            return InvitationOperationResult<InvitationView>.Failed(
                InvitationFailure.AlreadyExists);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return InvitationOperationResult<InvitationView>.Failed(
                InvitationFailure.ConcurrencyConflict);
        }
    }

    public async Task<InvitationOperationResult<AcceptedInvitation>> AcceptAsync(
        AcceptInvitationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumConcurrencyAttempts; attempt++)
        {
            try
            {
                return await unitOfWork.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        var locked = await LockDecisionContextAsync(
                            command.Actor,
                            command.InvitationId,
                            command.SessionId,
                            now,
                            transactionCancellationToken);
                        if (locked.Failure is not null)
                        {
                            return InvitationOperationResult<
                                AcceptedInvitation>.Failed(locked.Failure.Value);
                        }

                        var context = locked.Context!;
                        var classification = await ClassifyMutationAsync(
                            command.Actor,
                            context,
                            now,
                            requireNoMembership: true,
                            transactionCancellationToken);
                        if (classification is not null)
                        {
                            return InvitationOperationResult<
                                AcceptedInvitation>.Failed(classification.Value);
                        }

                        var role = ParseRole(context.Invitation.Role);
                        var memberId = OrganizationMemberId.New();
                        db.OrganizationMembers.Add(new OrganizationMemberEntity
                        {
                            Id = memberId.Value,
                            OrganizationId = context.Invitation.OrganizationId,
                            UserId = command.Actor.UserId.Value,
                            Role = role.Value,
                            JoinedAt = now,
                            UpdatedAt = now
                        });
                        if (context.Invitation.TeamId is not null)
                        {
                            db.TeamMembers.Add(new TeamMemberEntity
                            {
                                Id = TeamMemberId.New(now).Value,
                                OrganizationId = context.Invitation.OrganizationId,
                                TeamId = context.Invitation.TeamId.Value,
                                OrganizationMemberId = memberId.Value,
                                JoinedAt = now
                            });
                        }

                        context.Invitation.Status =
                            InvitationStatus.Accepted.Value;
                        context.Invitation.UpdatedAt = now;
                        context.Session!.ActiveOrganizationId =
                            context.Invitation.OrganizationId;
                        context.Session.UpdatedAt = now;
                        return InvitationOperationResult<
                            AcceptedInvitation>.Success(
                                new(
                                    command.InvitationId,
                                    new OrganizationId(
                                        context.Invitation.OrganizationId),
                                    context.Organization.Slug));
                    },
                    cancellationToken);
            }
            catch (Exception exception) when (
                IsRetryableConcurrencyFailure(exception) &&
                attempt < MaximumConcurrencyAttempts)
            {
                continue;
            }
            catch (Exception exception) when (IsUniqueViolation(
                exception,
                "ux_members_organization_id_user_id"))
            {
                return InvitationOperationResult<AcceptedInvitation>.Failed(
                    InvitationFailure.RecipientAlreadyMember);
            }
            catch (Exception exception) when (IsForeignKeyViolation(
                exception,
                "fk_team_members_teams_organization_id_team_id"))
            {
                return InvitationOperationResult<AcceptedInvitation>.Failed(
                    InvitationFailure.TeamInvalid);
            }
            catch (Exception exception) when (
                IsRetryableConcurrencyFailure(exception))
            {
                return InvitationOperationResult<AcceptedInvitation>.Failed(
                    InvitationFailure.ConcurrencyConflict);
            }
        }

        return InvitationOperationResult<AcceptedInvitation>.Failed(
            InvitationFailure.ConcurrencyConflict);
    }

    public async Task<InvitationOperationResult<InvitationDecision>> RejectAsync(
        RejectInvitationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumConcurrencyAttempts; attempt++)
        {
            try
            {
                return await unitOfWork.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        var locked = await LockDecisionContextAsync(
                            command.Actor,
                            command.InvitationId,
                            sessionId: null,
                            now,
                            transactionCancellationToken);
                        if (locked.Failure is not null)
                        {
                            return InvitationOperationResult<
                                InvitationDecision>.Failed(locked.Failure.Value);
                        }

                        var context = locked.Context!;
                        var classification = await ClassifyMutationAsync(
                            command.Actor,
                            context,
                            now,
                            requireNoMembership: false,
                            transactionCancellationToken);
                        if (classification is not null)
                        {
                            return InvitationOperationResult<
                                InvitationDecision>.Failed(classification.Value);
                        }

                        context.Invitation.Status =
                            InvitationStatus.Rejected.Value;
                        context.Invitation.UpdatedAt = now;
                        var view = MapView(
                            context.Invitation,
                            context.Organization.Name,
                            context.Organization.Slug,
                            context.Team?.Name,
                            context.InviterName,
                            now);
                        return InvitationOperationResult<
                            InvitationDecision>.Success(
                                new(
                                    view,
                                    InvitationDecisionState.Rejected,
                                    CanRespond: false));
                    },
                    cancellationToken);
            }
            catch (Exception exception) when (
                IsRetryableConcurrencyFailure(exception) &&
                attempt < MaximumConcurrencyAttempts)
            {
                continue;
            }
            catch (Exception exception) when (
                IsRetryableConcurrencyFailure(exception))
            {
                return InvitationOperationResult<InvitationDecision>.Failed(
                    InvitationFailure.ConcurrencyConflict);
            }
        }

        return InvitationOperationResult<InvitationDecision>.Failed(
            InvitationFailure.ConcurrencyConflict);
    }

    private async Task<InvitationFailure?> AuthorizeOrganizationReadAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var roleValue = await db.OrganizationMembers.AsNoTracking()
            .Where(row =>
                row.OrganizationId == organizationId &&
                row.UserId == actorUserId)
            .Select(row => row.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (roleValue is null)
        {
            return InvitationFailure.NotFound;
        }

        var role = ParseRole(roleValue);
        return OrganizationPermissionPolicy
            .GetCapabilities(role)
            .CanManageInvitations
                ? null
                : InvitationFailure.PermissionDenied;
    }

    private static IQueryable<InvitationEntity> ApplyDisplayFilter(
        IQueryable<InvitationEntity> query,
        InvitationDisplayState? filter,
        DateTimeOffset now)
    {
        if (filter is null)
        {
            return query;
        }

        if (filter == InvitationDisplayState.Pending)
        {
            return query.Where(row =>
                row.Status == InvitationStatus.Pending.Value &&
                row.ExpiresAt > now);
        }

        if (filter == InvitationDisplayState.Expired)
        {
            return query.Where(row =>
                row.Status == InvitationStatus.Pending.Value &&
                row.ExpiresAt <= now);
        }

        return query.Where(row => row.Status == filter.Value.Value);
    }

    private async Task<IReadOnlyList<InvitationView>> ReadViewsAsync(
        Guid[] invitationIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (invitationIds.Length == 0)
        {
            return [];
        }

        var rows = await ReadRowsQuery(invitationIds)
            .ToArrayAsync(cancellationToken);
        var byId = rows.ToDictionary(row => row.Id);
        return invitationIds
            .Select(id => MapView(byId[id], now))
            .ToArray();
    }

    private async Task<InvitationView> ReadViewAsync(
        Guid invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        MapView(
            await ReadRowsQuery([invitationId]).SingleAsync(cancellationToken),
            now);

    private IQueryable<InvitationReadRow> ReadRowsQuery(Guid[] invitationIds) =>
        from invitation in db.Invitations.AsNoTracking()
        join organization in db.Organizations.AsNoTracking()
            on invitation.OrganizationId equals organization.Id
        join inviter in db.Users.AsNoTracking()
            on invitation.InviterUserId equals inviter.Id
        where invitationIds.Contains(invitation.Id)
        select new InvitationReadRow(
            invitation.Id,
            invitation.OrganizationId,
            organization.Name,
            organization.Slug,
            invitation.TeamId,
            invitation.TeamId == null
                ? null
                : db.Teams.AsNoTracking()
                    .Where(team =>
                        team.OrganizationId == invitation.OrganizationId &&
                        team.Id == invitation.TeamId.Value)
                    .Select(team => team.Name)
                    .SingleOrDefault(),
            invitation.Email,
            invitation.Role,
            invitation.Status,
            invitation.ExpiresAt,
            invitation.CreatedAt,
            invitation.InviterUserId,
            inviter.DisplayName);

    private static InvitationView MapView(
        InvitationReadRow row,
        DateTimeOffset now)
    {
        var status = ParseStatus(row.Status);
        return new InvitationView(
            new InvitationId(row.Id),
            new OrganizationId(row.OrganizationId),
            row.OrganizationName,
            row.OrganizationSlug,
            row.TeamId is null ? null : new TeamId(row.TeamId.Value),
            row.TeamName,
            row.Email,
            ParseRole(row.Role),
            status,
            InvitationPolicy.GetDisplayState(status, row.ExpiresAt, now),
            row.ExpiresAt,
            row.CreatedAt,
            new UserId(row.InviterUserId),
            row.InviterName);
    }

    private static InvitationView MapView(
        InvitationEntity invitation,
        string organizationName,
        string organizationSlug,
        string? teamName,
        string inviterName,
        DateTimeOffset now) =>
        MapView(
            new InvitationReadRow(
                invitation.Id,
                invitation.OrganizationId,
                organizationName,
                organizationSlug,
                invitation.TeamId,
                teamName,
                invitation.Email,
                invitation.Role,
                invitation.Status,
                invitation.ExpiresAt,
                invitation.CreatedAt,
                invitation.InviterUserId,
                inviterName),
            now);

    private async Task<InvitationDecisionState> ClassifyDecisionAsync(
        InvitationActor actor,
        InvitationEntity invitation,
        InvitationView view,
        CancellationToken cancellationToken)
    {
        if (view.DisplayState == InvitationDisplayState.Accepted)
        {
            return InvitationDecisionState.Accepted;
        }

        if (view.DisplayState == InvitationDisplayState.Rejected)
        {
            return InvitationDecisionState.Rejected;
        }

        if (view.DisplayState == InvitationDisplayState.Canceled)
        {
            return InvitationDecisionState.Canceled;
        }

        if (view.DisplayState == InvitationDisplayState.Expired)
        {
            return InvitationDecisionState.Expired;
        }

        if (!actor.IsEmailVerified)
        {
            return InvitationDecisionState.EmailVerificationRequired;
        }

        var domains = await ReadAllowedDomainsAsync(
            invitation.OrganizationId,
            cancellationToken);
        if (!OrganizationEmailDomainPolicy.IsAllowed(
                invitation.Email,
                domains))
        {
            return InvitationDecisionState.DomainRestricted;
        }

        var alreadyMember = await db.OrganizationMembers.AsNoTracking()
            .AnyAsync(
                row =>
                    row.OrganizationId == invitation.OrganizationId &&
                    row.UserId == actor.UserId.Value,
                cancellationToken);
        return alreadyMember
            ? InvitationDecisionState.AlreadyMember
            : InvitationDecisionState.Pending;
    }

    private async Task<LockedDecisionResult> LockDecisionContextAsync(
        InvitationActor actor,
        InvitationId invitationId,
        SessionId? sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Invitations.AsNoTracking()
            .Where(row => row.Id == invitationId.Value)
            .Select(row => new InvitationCandidate(row.OrganizationId))
            .SingleOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            return LockedDecisionResult.Failed(InvitationFailure.NotFound);
        }

        var discoveredOrganizationIds = await db.OrganizationMembers
            .AsNoTracking()
            .Where(row => row.UserId == actor.UserId.Value)
            .OrderBy(row => row.OrganizationId)
            .Select(row => row.OrganizationId)
            .ToArrayAsync(cancellationToken);
        var affectedOrganizationIds = discoveredOrganizationIds
            .Append(candidate.OrganizationId)
            .Distinct()
            .ToArray();
        var lockedOrganizations = await LockOrganizationsAsync(
            affectedOrganizationIds,
            cancellationToken);
        if (lockedOrganizations.Count != affectedOrganizationIds.Length ||
            lockedOrganizations.Any(row =>
                !affectedOrganizationIds.Contains(row.Id)))
        {
            throw new InvitationRetryException();
        }

        var target = await LockUserAsync(
            actor.UserId.Value,
            cancellationToken);
        if (target is null)
        {
            return LockedDecisionResult.Failed(InvitationFailure.NotFound);
        }

        var invitation = await LockInvitationAsync(
            invitationId.Value,
            cancellationToken);
        if (invitation is null)
        {
            return LockedDecisionResult.Failed(InvitationFailure.NotFound);
        }

        TeamEntity? team = null;
        if (invitation.TeamId is not null)
        {
            team = await LockTeamAsync(
                invitation.OrganizationId,
                invitation.TeamId.Value,
                cancellationToken);
        }

        var memberships = await LockUserMembershipsAsync(
            actor.UserId.Value,
            cancellationToken);
        if (!memberships
                .Select(row => row.OrganizationId)
                .SequenceEqual(discoveredOrganizationIds))
        {
            throw new InvitationRetryException();
        }

        var organization = lockedOrganizations.Single(
            row => row.Id == invitation.OrganizationId);
        AuthSessionEntity? session = null;
        if (sessionId is not null)
        {
            session = await LockCurrentSessionAsync(
                actor.UserId.Value,
                sessionId.Value.Value,
                now,
                cancellationToken);
            if (session is null)
            {
                return LockedDecisionResult.Failed(InvitationFailure.NotFound);
            }
        }

        await AcquireNameNamespaceLockAsync(
            organization.Name,
            cancellationToken);

        var inviterName = await db.Users.AsNoTracking()
            .Where(user => user.Id == invitation.InviterUserId)
            .Select(user => user.DisplayName)
            .SingleAsync(cancellationToken);
        return LockedDecisionResult.Success(new LockedDecisionContext(
            organization,
            target,
            invitation,
            team,
            memberships,
            session,
            inviterName));
    }

    private async Task<InvitationFailure?> ClassifyMutationAsync(
        InvitationActor actor,
        LockedDecisionContext context,
        DateTimeOffset now,
        bool requireNoMembership,
        CancellationToken cancellationToken)
    {
        var currentEmail = NormalizeEmail(
            context.Target.NormalizedEmail ?? string.Empty);
        if (!RecipientMatches(actor, context.Invitation.Email) ||
            !string.Equals(
                currentEmail,
                context.Invitation.Email,
                StringComparison.Ordinal))
        {
            return InvitationFailure.RecipientMismatch;
        }

        if (!actor.IsEmailVerified || !context.Target.EmailConfirmed)
        {
            return InvitationFailure.EmailVerificationRequired;
        }

        var status = ParseStatus(context.Invitation.Status);
        if (status == InvitationStatus.Pending &&
            context.Invitation.ExpiresAt <= now)
        {
            return InvitationFailure.Expired;
        }

        if (status != InvitationStatus.Pending)
        {
            return InvitationFailure.NotPending;
        }

        var domains = await ReadAllowedDomainsAsync(
            context.Invitation.OrganizationId,
            cancellationToken);
        if (!OrganizationEmailDomainPolicy.IsAllowed(
                context.Invitation.Email,
                domains))
        {
            return InvitationFailure.DomainRestricted;
        }

        if (context.Invitation.TeamId is not null && context.Team is null)
        {
            return InvitationFailure.TeamInvalid;
        }

        _ = ParseRole(context.Invitation.Role);
        if (!requireNoMembership)
        {
            return null;
        }

        if (context.Memberships.Any(row =>
                row.OrganizationId == context.Invitation.OrganizationId))
        {
            return InvitationFailure.RecipientAlreadyMember;
        }

        if (await AccessibleNameExistsAsync(
                actor.UserId.Value,
                context.Organization.Name,
                context.Invitation.OrganizationId,
                cancellationToken))
        {
            return InvitationFailure.MembershipConflict;
        }

        return null;
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
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    private Task<InvitationEntity?> LockPendingDuplicateAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken) =>
        db.Invitations
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.invitations
                 WHERE organization_id = {organizationId}
                   AND email = {email}
                   AND status = 'pending'
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<InvitationEntity?> LockInvitationAsync(
        Guid invitationId,
        CancellationToken cancellationToken) =>
        db.Invitations
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.invitations
                 WHERE id = {invitationId}
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<ApplicationUser?> LockUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        db.Users
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM auth.users
                 WHERE id = {userId}
                 FOR UPDATE
                 """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    private Task<List<OrganizationEntity>> LockOrganizationsAsync(
        Guid[] organizationIds,
        CancellationToken cancellationToken) =>
        db.Organizations
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.organizations
                 WHERE id = ANY ({organizationIds})
                 ORDER BY id
                 FOR UPDATE
                 """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    private Task<List<OrganizationMemberEntity>> LockUserMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM organizations.members
                 WHERE user_id = {userId}
                 ORDER BY organization_id, id
                 FOR UPDATE
                 """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    private Task<AuthSessionEntity?> LockCurrentSessionAsync(
        Guid userId,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        db.Sessions
            .FromSqlInterpolated(
                $"""
                 SELECT *
                 FROM auth.sessions
                 WHERE id = {sessionId}
                   AND user_id = {userId}
                   AND expires_at > {now}
                 FOR UPDATE
                 """)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task AcquireNameNamespaceLockAsync(
        string name,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlAsync(
            $"""
             SELECT pg_advisory_xact_lock(
                 {OrganizationNameAdvisoryLockNamespace},
                 hashtext(lower({name})))
             """,
            cancellationToken);
    }

    private Task<bool> AccessibleNameExistsAsync(
        Guid userId,
        string name,
        Guid excludedOrganizationId,
        CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>(
                $"""
                 SELECT EXISTS (
                     SELECT 1
                     FROM organizations.organizations AS organization
                     INNER JOIN organizations.members AS membership
                         ON membership.organization_id = organization.id
                     WHERE membership.user_id = {userId}
                       AND organization.id <> {excludedOrganizationId}
                       AND lower(organization.name) = lower({name})
                 ) AS "Value"
                 """)
            .SingleAsync(cancellationToken);

    private Task<bool> IsCurrentMemberByEmailAsync(
        Guid organizationId,
        string email,
        CancellationToken cancellationToken)
    {
        var normalized = email.ToUpperInvariant();
        return (
            from primaryEmail in db.UserEmails.AsNoTracking()
            join membership in db.OrganizationMembers.AsNoTracking()
                on primaryEmail.UserId equals membership.UserId
            where primaryEmail.IsPrimary &&
                  primaryEmail.NormalizedEmail == normalized &&
                  membership.OrganizationId == organizationId
            select membership.Id).AnyAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ReadAllowedDomainsAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await db.OrganizationAllowedEmailDomains.AsNoTracking()
            .Where(row => row.OrganizationId == organizationId)
            .OrderBy(row => row.Domain)
            .Select(row => row.Domain)
            .ToArrayAsync(cancellationToken);

    private static bool RecipientMatches(
        InvitationActor actor,
        string invitationEmail) =>
        string.Equals(
            NormalizeEmail(actor.NormalizedPrimaryEmail),
            invitationEmail,
            StringComparison.Ordinal);

    private static string NormalizeEmail(string value) =>
        value.Trim().ToLowerInvariant();

    private static OrganizationRole ParseRole(string value) =>
        OrganizationRole.TryParse(value, out var role)
            ? role
            : throw new InvalidOperationException(
                "The database contains an unknown organization role.");

    private static InvitationStatus ParseStatus(string value) =>
        InvitationStatus.TryParse(value, out var status)
            ? status
            : throw new InvalidOperationException(
                "The database contains an unknown invitation status.");

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

    private static bool IsForeignKeyViolation(
        Exception exception,
        string constraintName)
    {
        var postgres = FindPostgresException(exception);
        return postgres?.SqlState == PostgresErrorCodes.ForeignKeyViolation &&
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

    private static bool IsRetryableConcurrencyFailure(Exception exception) =>
        exception is InvitationRetryException ||
        IsConcurrencyFailure(exception);

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

    private sealed record InvitationReadRow(
        Guid Id,
        Guid OrganizationId,
        string OrganizationName,
        string OrganizationSlug,
        Guid? TeamId,
        string? TeamName,
        string Email,
        string Role,
        string Status,
        DateTimeOffset ExpiresAt,
        DateTimeOffset CreatedAt,
        Guid InviterUserId,
        string InviterName);

    private sealed record InvitationCandidate(Guid OrganizationId);

    private sealed record LockedDecisionContext(
        OrganizationEntity Organization,
        ApplicationUser Target,
        InvitationEntity Invitation,
        TeamEntity? Team,
        IReadOnlyList<OrganizationMemberEntity> Memberships,
        AuthSessionEntity? Session,
        string InviterName);

    private sealed record LockedDecisionResult(
        LockedDecisionContext? Context,
        InvitationFailure? Failure)
    {
        internal static LockedDecisionResult Success(
            LockedDecisionContext context) => new(context, null);

        internal static LockedDecisionResult Failed(
            InvitationFailure failure) => new(null, failure);
    }

    private sealed class InvitationRetryException : Exception;
}
