using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Common.Ports;
using Template.Application.Organizations;
using Template.Application.Organizations.Ports;
using Template.Domain.Authentication;
using Template.Domain.Organizations;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Organizations;

internal sealed class EfOrganizationStore(
    TemplateDbContext db,
    IApplicationUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IOrganizationStore
{
    private const int MaximumSlugAttempts = 5;

    public async Task<
        OrganizationStorePage<OrganizationSummary, OrganizationCursorPosition>>
        ListAsync(
            UserId actorUserId,
            OrganizationCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        var query =
            from organization in db.Organizations.AsNoTracking()
            join membership in db.OrganizationMembers.AsNoTracking()
                on organization.Id equals membership.OrganizationId
            where membership.UserId == actorUserId.Value
            select new
            {
                organization.Id,
                organization.Name,
                NormalizedName = organization.Name.ToLower(),
                organization.Slug,
                organization.CreatedAt,
                organization.UpdatedAt,
                membership.Role
            };

        if (after is not null)
        {
            query = query.Where(row =>
                string.Compare(
                    row.NormalizedName,
                    after.NormalizedName) > 0 ||
                (row.NormalizedName == after.NormalizedName &&
                 row.Id.CompareTo(after.Id.Value) > 0));
        }

        var rows = await query
            .OrderBy(row => row.NormalizedName)
            .ThenBy(row => row.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var pageRows = hasMore ? rows[..limit] : rows;
        var items = pageRows
            .Select(row => MapSummary(new OrganizationReadRow(
                row.Id,
                row.Name,
                row.NormalizedName,
                row.Slug,
                row.CreatedAt,
                row.UpdatedAt,
                row.Role)))
            .ToArray();
        var next = hasMore
            ? new OrganizationCursorPosition(
                pageRows[^1].NormalizedName,
                new OrganizationId(pageRows[^1].Id))
            : null;
        return new OrganizationStorePage<
            OrganizationSummary,
            OrganizationCursorPosition>(items, next);
    }

    public async Task<OrganizationOperationResult<OrganizationDetail>>
        GetByKeyAsync(
            UserId actorUserId,
            string organizationKey,
            CancellationToken cancellationToken)
    {
        var query =
            from organization in db.Organizations.AsNoTracking()
            join membership in db.OrganizationMembers.AsNoTracking()
                on organization.Id equals membership.OrganizationId
            where membership.UserId == actorUserId.Value
            select new
            {
                organization.Id,
                organization.Name,
                NormalizedName = organization.Name.ToLower(),
                organization.Slug,
                organization.CreatedAt,
                organization.UpdatedAt,
                membership.Role
            };

        var parsedId = Guid.Empty;
        var isId = Guid.TryParseExact(organizationKey, "D", out parsedId);
        var row = isId
            ? await query
                .Where(value =>
                    value.Id == parsedId ||
                    value.Slug == organizationKey)
                .OrderByDescending(value => value.Id == parsedId)
                .ThenBy(value => value.Id)
                .FirstOrDefaultAsync(cancellationToken)
            : await query.SingleOrDefaultAsync(
                value => value.Slug == organizationKey,
                cancellationToken);

        if (row is null)
        {
            return OrganizationOperationResult<OrganizationDetail>.Failed(
                OrganizationFailure.NotFound);
        }

        var domains = await ReadAllowedDomainsAsync(
            row.Id,
            cancellationToken);
        return OrganizationOperationResult<OrganizationDetail>.Success(
            MapDetail(
                new OrganizationReadRow(
                    row.Id,
                    row.Name,
                    row.NormalizedName,
                    row.Slug,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.Role),
                domains));
    }

    public async Task<OrganizationOperationResult<OrganizationDetail>>
        CreateAsync(
            CreateOrganizationCommand command,
            CancellationToken cancellationToken)
    {
        var slugBase = OrganizationSlug.GenerateBase(command.Name);
        for (var attempt = 1; attempt <= MaximumSlugAttempts; attempt++)
        {
            var candidate = attempt == 1
                ? slugBase
                : $"{slugBase}-{attempt}";
            try
            {
                var result = await unitOfWork.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        if (await LockUserAsync(
                                command.ActorUserId.Value,
                                transactionCancellationToken) is null)
                        {
                            return OrganizationOperationResult<
                                OrganizationDetail>.Failed(
                                OrganizationFailure.NotFound);
                        }

                        var session = await LockCurrentSessionAsync(
                            command.ActorUserId.Value,
                            command.SessionId.Value,
                            timeProvider.GetUtcNow(),
                            transactionCancellationToken);
                        if (session is null)
                        {
                            return OrganizationOperationResult<
                                OrganizationDetail>.Failed(
                                OrganizationFailure.NotFound);
                        }

                        var nameExists = await AccessibleNameExistsAsync(
                            command.ActorUserId.Value,
                            command.Name,
                            excludedOrganizationId: null,
                            transactionCancellationToken);
                        if (nameExists)
                        {
                            return OrganizationOperationResult<
                                OrganizationDetail>.Failed(
                                OrganizationFailure.NameConflict);
                        }

                        if (await db.Organizations.AsNoTracking().AnyAsync(
                                organization => organization.Slug == candidate,
                                transactionCancellationToken))
                        {
                            return OrganizationOperationResult<
                                OrganizationDetail>.Failed(
                                OrganizationFailure.SlugConflict);
                        }

                        var now = timeProvider.GetUtcNow();
                        var organizationId = OrganizationId.New();
                        db.Organizations.Add(new OrganizationEntity
                        {
                            Id = organizationId.Value,
                            Name = command.Name,
                            Slug = candidate,
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                        db.OrganizationMembers.Add(new OrganizationMemberEntity
                        {
                            Id = OrganizationMemberId.New().Value,
                            OrganizationId = organizationId.Value,
                            UserId = command.ActorUserId.Value,
                            Role = OrganizationRole.Owner.Value,
                            JoinedAt = now,
                            UpdatedAt = now
                        });
                        session.ActiveOrganizationId = organizationId.Value;

                        OrganizationSlug.TryCreate(candidate, out var slug);
                        return OrganizationOperationResult<
                            OrganizationDetail>.Success(
                            new OrganizationDetail(
                                organizationId,
                                command.Name,
                                slug,
                                now,
                                now,
                                OrganizationRole.Owner,
                                OrganizationPermissionPolicy.GetCapabilities(
                                    OrganizationRole.Owner),
                                []));
                    },
                    cancellationToken);

                if (result.Failure != OrganizationFailure.SlugConflict ||
                    attempt == MaximumSlugAttempts)
                {
                    return result;
                }
            }
            catch (Exception exception) when (
                IsUniqueViolation(exception, "ux_organizations_slug"))
            {
                if (attempt == MaximumSlugAttempts)
                {
                    return OrganizationOperationResult<
                        OrganizationDetail>.Failed(
                        OrganizationFailure.SlugConflict);
                }
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                return OrganizationOperationResult<OrganizationDetail>.Failed(
                    OrganizationFailure.ConcurrencyConflict);
            }
        }

        return OrganizationOperationResult<OrganizationDetail>.Failed(
            OrganizationFailure.SlugConflict);
    }

    public async Task<OrganizationOperationResult<OrganizationDetail>>
        UpdateAsync(
            UpdateOrganizationCommand command,
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
                        return OrganizationOperationResult<
                            OrganizationDetail>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    if (await LockUserAsync(
                            command.ActorUserId.Value,
                            transactionCancellationToken) is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationDetail>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actor = await LockMembershipAsync(
                        command.OrganizationId.Value,
                        command.ActorUserId.Value,
                        transactionCancellationToken);
                    if (actor is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationDetail>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actorRole = ParseRole(actor.Role);
                    if (!OrganizationPermissionPolicy
                        .GetCapabilities(actorRole)
                        .CanUpdateOrganization)
                    {
                        return OrganizationOperationResult<
                            OrganizationDetail>.Failed(
                            OrganizationFailure.PermissionDenied);
                    }

                    if (command.Name is not null)
                    {
                        var nameExists = await AccessibleNameExistsAsync(
                            command.ActorUserId.Value,
                            command.Name,
                            command.OrganizationId.Value,
                            transactionCancellationToken);
                        if (nameExists)
                        {
                            return OrganizationOperationResult<
                                OrganizationDetail>.Failed(
                                OrganizationFailure.NameConflict);
                        }
                    }

                    if (command.Slug is not null &&
                        await db.Organizations.AsNoTracking().AnyAsync(
                            other =>
                                other.Id != command.OrganizationId.Value &&
                                other.Slug == command.Slug.Value.Value,
                            transactionCancellationToken))
                    {
                        return OrganizationOperationResult<
                            OrganizationDetail>.Failed(
                            OrganizationFailure.SlugConflict);
                    }

                    var name = command.Name ?? organization.Name;
                    var slugValue = command.Slug?.Value ?? organization.Slug;
                    var now = timeProvider.GetUtcNow();
                    await db.Organizations
                        .Where(row => row.Id == command.OrganizationId.Value)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(row => row.Name, name)
                                .SetProperty(row => row.Slug, slugValue)
                                .SetProperty(row => row.UpdatedAt, now),
                            transactionCancellationToken);

                    IReadOnlyList<string> domains;
                    if (command.AllowedEmailDomains is null)
                    {
                        domains = await ReadAllowedDomainsAsync(
                            command.OrganizationId.Value,
                            transactionCancellationToken);
                    }
                    else
                    {
                        await db.OrganizationAllowedEmailDomains
                            .Where(row =>
                                row.OrganizationId ==
                                command.OrganizationId.Value)
                            .ExecuteDeleteAsync(transactionCancellationToken);
                        db.OrganizationAllowedEmailDomains.AddRange(
                            command.AllowedEmailDomains.Select(domain =>
                                new OrganizationAllowedEmailDomainEntity
                                {
                                    OrganizationId =
                                        command.OrganizationId.Value,
                                    Domain = domain
                                }));
                        domains = command.AllowedEmailDomains
                            .Order(StringComparer.Ordinal)
                            .ToArray();
                    }

                    OrganizationSlug.TryCreate(slugValue, out var slug);
                    return OrganizationOperationResult<
                        OrganizationDetail>.Success(
                        new OrganizationDetail(
                            command.OrganizationId,
                            name,
                            slug,
                            organization.CreatedAt,
                            now,
                            actorRole,
                            OrganizationPermissionPolicy.GetCapabilities(
                                actorRole),
                            domains));
                },
                cancellationToken);
        }
        catch (Exception exception) when (
            IsUniqueViolation(exception, "ux_organizations_slug"))
        {
            return OrganizationOperationResult<OrganizationDetail>.Failed(
                OrganizationFailure.SlugConflict);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return OrganizationOperationResult<OrganizationDetail>.Failed(
                OrganizationFailure.ConcurrencyConflict);
        }
    }

    public async Task<OrganizationOperationResult<OrganizationDeletion>>
        DeleteAsync(
            DeleteOrganizationCommand command,
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
                        return OrganizationOperationResult<
                            OrganizationDeletion>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var accessibleMemberships =
                        await LockAccessibleMembershipsAsync(
                            command.ActorUserId.Value,
                            transactionCancellationToken);
                    var actor = accessibleMemberships.SingleOrDefault(
                        membership =>
                            membership.OrganizationId ==
                            command.OrganizationId.Value);
                    if (actor is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationDeletion>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actorRole = ParseRole(actor.Role);
                    if (!OrganizationPermissionPolicy
                        .GetCapabilities(actorRole)
                        .CanDeleteOrganization)
                    {
                        return OrganizationOperationResult<
                            OrganizationDeletion>.Failed(
                            OrganizationFailure.PermissionDenied);
                    }

                    if (!string.Equals(
                            organization.Name,
                            command.ConfirmationName,
                            StringComparison.Ordinal))
                    {
                        return OrganizationOperationResult<
                            OrganizationDeletion>.Failed(
                            OrganizationFailure.ConfirmationMismatch);
                    }

                    if (accessibleMemberships.Count <= 1)
                    {
                        return OrganizationOperationResult<
                            OrganizationDeletion>.Failed(
                            OrganizationFailure.LastAccessibleOrganization);
                    }

                    await db.Organizations
                        .Where(row => row.Id == command.OrganizationId.Value)
                        .ExecuteDeleteAsync(transactionCancellationToken);
                    return OrganizationOperationResult<
                        OrganizationDeletion>.Success(
                        new OrganizationDeletion(command.OrganizationId));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return OrganizationOperationResult<OrganizationDeletion>.Failed(
                OrganizationFailure.ConcurrencyConflict);
        }
    }

    public async Task<OrganizationOperationResult<ActiveOrganization>>
        SetActiveAsync(
            SetActiveOrganizationCommand command,
            CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    var now = timeProvider.GetUtcNow();
                    var changed = await db.Sessions
                        .Where(session =>
                            session.Id == command.SessionId.Value &&
                            session.UserId == command.ActorUserId.Value &&
                            session.ExpiresAt > now &&
                            db.OrganizationMembers.Any(membership =>
                                membership.OrganizationId ==
                                command.OrganizationId.Value &&
                                membership.UserId ==
                                command.ActorUserId.Value))
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                session => session.ActiveOrganizationId,
                                command.OrganizationId.Value),
                            transactionCancellationToken);
                    return changed == 1
                        ? OrganizationOperationResult<
                            ActiveOrganization>.Success(
                            new ActiveOrganization(command.OrganizationId))
                        : OrganizationOperationResult<
                            ActiveOrganization>.Failed(
                            OrganizationFailure.NotFound);
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return OrganizationOperationResult<ActiveOrganization>.Failed(
                OrganizationFailure.ConcurrencyConflict);
        }
    }

    public async Task<
        OrganizationOperationResult<
            OrganizationStorePage<
                OrganizationMember,
                OrganizationMemberCursorPosition>>>
        ListMembersAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            OrganizationMemberCursorPosition? after,
            int limit,
            CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    if (await LockOrganizationAsync(
                            organizationId.Value,
                            transactionCancellationToken) is null
                        || await LockMembershipAsync(
                            organizationId.Value,
                            actorUserId.Value,
                            transactionCancellationToken) is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationStorePage<
                                OrganizationMember,
                                OrganizationMemberCursorPosition>>.Failed(
                                    OrganizationFailure.NotFound);
                    }

                    var domains = await ReadAllowedDomainsAsync(
                        organizationId.Value,
                        transactionCancellationToken);
                    var query =
                        from membership in db.OrganizationMembers.AsNoTracking()
                        join user in db.Users.AsNoTracking()
                            on membership.UserId equals user.Id
                        where membership.OrganizationId ==
                            organizationId.Value
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
                            (row.JoinedAt == after.JoinedAt &&
                             row.Id.CompareTo(after.Id.Value) > 0));
                    }

                    var rows = await query
                        .OrderBy(row => row.JoinedAt)
                        .ThenBy(row => row.Id)
                        .Take(limit + 1)
                        .ToListAsync(transactionCancellationToken);
                    var hasMore = rows.Count > limit;
                    var pageRows = hasMore ? rows[..limit] : rows;
                    var items = pageRows
                        .Select(row => MapMember(
                            new MemberReadRow(
                                row.Id,
                                row.UserId,
                                row.Name,
                                row.Email,
                                row.ImageUrl,
                                row.Role,
                                row.JoinedAt),
                            domains))
                        .ToArray();
                    var next = hasMore
                        ? new OrganizationMemberCursorPosition(
                            pageRows[^1].JoinedAt,
                            new OrganizationMemberId(pageRows[^1].Id))
                        : null;
                    return OrganizationOperationResult<
                        OrganizationStorePage<
                            OrganizationMember,
                            OrganizationMemberCursorPosition>>.Success(
                                new OrganizationStorePage<
                                    OrganizationMember,
                                    OrganizationMemberCursorPosition>(
                                        items,
                                        next));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return OrganizationOperationResult<
                OrganizationStorePage<
                    OrganizationMember,
                    OrganizationMemberCursorPosition>>.Failed(
                        OrganizationFailure.ConcurrencyConflict);
        }
    }

    public async Task<OrganizationOperationResult<OrganizationMember>>
        AddMemberAsync(
            AddOrganizationMemberCommand command,
            CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    if (await LockOrganizationAsync(
                            command.OrganizationId.Value,
                            transactionCancellationToken) is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actor = await LockMembershipAsync(
                        command.OrganizationId.Value,
                        command.ActorUserId.Value,
                        transactionCancellationToken);
                    if (actor is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actorRole = ParseRole(actor.Role);
                    if (!OrganizationPermissionPolicy.CanAssign(
                            actorRole,
                            command.Role))
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.RoleAssignmentForbidden);
                    }

                    var target = await LockUserAsync(
                        command.TargetUserId.Value,
                        transactionCancellationToken);
                    if (target is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.TargetUserNotFound);
                    }

                    if (await LockMembershipAsync(
                            command.OrganizationId.Value,
                            command.TargetUserId.Value,
                            transactionCancellationToken) is not null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.MemberAlreadyExists);
                    }

                    var domains = await ReadAllowedDomainsAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken);
                    var email = target.Email ?? string.Empty;
                    var eligibility = OrganizationEmailDomainPolicy.Evaluate(
                        email,
                        domains);
                    if (!eligibility.IsAllowed &&
                        !command.AcknowledgeDomainRestriction)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.DomainAcknowledgementRequired,
                            new OrganizationDomainAcknowledgement(
                                email,
                                eligibility.EmailDomain,
                                domains));
                    }

                    var now = timeProvider.GetUtcNow();
                    var memberId = OrganizationMemberId.New();
                    db.OrganizationMembers.Add(new OrganizationMemberEntity
                    {
                        Id = memberId.Value,
                        OrganizationId = command.OrganizationId.Value,
                        UserId = command.TargetUserId.Value,
                        Role = command.Role.Value,
                        JoinedAt = now,
                        UpdatedAt = now
                    });
                    return OrganizationOperationResult<
                        OrganizationMember>.Success(
                        new OrganizationMember(
                            memberId,
                            command.TargetUserId,
                            target.DisplayName,
                            email,
                            target.ImageUrl,
                            command.Role,
                            now,
                            eligibility.EmailDomain,
                            !eligibility.IsAllowed));
                },
                cancellationToken);
        }
        catch (Exception exception) when (
            IsUniqueViolation(
                exception,
                "ux_members_organization_id_user_id"))
        {
            return OrganizationOperationResult<OrganizationMember>.Failed(
                OrganizationFailure.MemberAlreadyExists);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return OrganizationOperationResult<OrganizationMember>.Failed(
                OrganizationFailure.ConcurrencyConflict);
        }
    }

    public async Task<OrganizationOperationResult<OrganizationMember>>
        UpdateMemberRoleAsync(
            UpdateOrganizationMemberRoleCommand command,
            CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(
                async transactionCancellationToken =>
                {
                    if (await LockOrganizationAsync(
                            command.OrganizationId.Value,
                            transactionCancellationToken) is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actor = await LockMembershipAsync(
                        command.OrganizationId.Value,
                        command.ActorUserId.Value,
                        transactionCancellationToken);
                    if (actor is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.NotFound);
                    }

                    var actorRole = ParseRole(actor.Role);
                    if (!OrganizationPermissionPolicy
                        .GetCapabilities(actorRole)
                        .CanUpdateMemberRoles)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.RoleAssignmentForbidden);
                    }

                    var target = await LockMembershipByIdAsync(
                        command.OrganizationId.Value,
                        command.MemberId.Value,
                        transactionCancellationToken);
                    if (target is null)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.MemberNotFound);
                    }

                    if (target.UserId == command.ActorUserId.Value)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.RoleAssignmentForbidden);
                    }

                    var targetRole = ParseRole(target.Role);
                    if (targetRole == command.Role)
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.MemberRoleUnchanged);
                    }

                    var owners = await LockOwnersAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken);
                    if (!OrganizationPermissionPolicy.CanChangeRole(
                            actorRole,
                            command.ActorUserId.Value,
                            target.UserId,
                            targetRole,
                            command.Role,
                            owners.Count))
                    {
                        return OrganizationOperationResult<
                            OrganizationMember>.Failed(
                            OrganizationFailure.RoleAssignmentForbidden);
                    }

                    var now = timeProvider.GetUtcNow();
                    await db.OrganizationMembers
                        .Where(row => row.Id == command.MemberId.Value)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(
                                    row => row.Role,
                                    command.Role.Value)
                                .SetProperty(row => row.UpdatedAt, now),
                            transactionCancellationToken);
                    var user = await db.Users.AsNoTracking()
                        .Where(row => row.Id == target.UserId)
                        .Select(row => new
                        {
                            row.DisplayName,
                            Email = row.Email ?? string.Empty,
                            row.ImageUrl
                        })
                        .SingleAsync(transactionCancellationToken);
                    var domains = await ReadAllowedDomainsAsync(
                        command.OrganizationId.Value,
                        transactionCancellationToken);
                    var eligibility = OrganizationEmailDomainPolicy.Evaluate(
                        user.Email,
                        domains);
                    return OrganizationOperationResult<
                        OrganizationMember>.Success(
                        new OrganizationMember(
                            command.MemberId,
                            new UserId(target.UserId),
                            user.DisplayName,
                            user.Email,
                            user.ImageUrl,
                            command.Role,
                            target.JoinedAt,
                            eligibility.EmailDomain,
                            !eligibility.IsAllowed));
                },
                cancellationToken);
        }
        catch (Exception exception) when (IsConcurrencyFailure(exception))
        {
            return OrganizationOperationResult<OrganizationMember>.Failed(
                OrganizationFailure.ConcurrencyConflict);
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

    private Task<bool> AccessibleNameExistsAsync(
        Guid actorUserId,
        string name,
        Guid? excludedOrganizationId,
        CancellationToken cancellationToken) =>
        excludedOrganizationId is null
            ? db.Database.SqlQuery<bool>(
                    $"""
                    SELECT EXISTS (
                        SELECT 1
                        FROM organizations.organizations AS organization
                        INNER JOIN organizations.members AS membership
                            ON membership.organization_id = organization.id
                        WHERE membership.user_id = {actorUserId}
                          AND lower(organization.name) = lower({name})
                    ) AS "Value"
                    """)
                .SingleAsync(cancellationToken)
            : db.Database.SqlQuery<bool>(
                    $"""
                    SELECT EXISTS (
                        SELECT 1
                        FROM organizations.organizations AS organization
                        INNER JOIN organizations.members AS membership
                            ON membership.organization_id = organization.id
                        WHERE membership.user_id = {actorUserId}
                          AND organization.id <> {excludedOrganizationId.Value}
                          AND lower(organization.name) = lower({name})
                    ) AS "Value"
                    """)
                .SingleAsync(cancellationToken);

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

    private Task<OrganizationMemberEntity?> LockMembershipByIdAsync(
        Guid organizationId,
        Guid memberId,
        CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM organizations.members
                WHERE organization_id = {organizationId}
                  AND id = {memberId}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

    private Task<List<OrganizationMemberEntity>> LockAccessibleMembershipsAsync(
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

    private Task<List<OrganizationMemberEntity>> LockOwnersAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        db.OrganizationMembers
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM organizations.members
                WHERE organization_id = {organizationId}
                  AND role = 'owner'
                ORDER BY id
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<string>> ReadAllowedDomainsAsync(
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await db.OrganizationAllowedEmailDomains.AsNoTracking()
            .Where(row => row.OrganizationId == organizationId)
            .OrderBy(row => row.Domain)
            .Select(row => row.Domain)
            .ToArrayAsync(cancellationToken);

    private static OrganizationSummary MapSummary(OrganizationReadRow row)
    {
        var role = ParseRole(row.Role);
        OrganizationSlug.TryCreate(row.Slug, out var slug);
        return new OrganizationSummary(
            new OrganizationId(row.Id),
            row.Name,
            slug,
            row.CreatedAt,
            row.UpdatedAt,
            role,
            OrganizationPermissionPolicy.GetCapabilities(role));
    }

    private static OrganizationDetail MapDetail(
        OrganizationReadRow row,
        IReadOnlyList<string> domains)
    {
        var summary = MapSummary(row);
        return new OrganizationDetail(
            summary.Id,
            summary.Name,
            summary.Slug,
            summary.CreatedAt,
            summary.UpdatedAt,
            summary.CurrentRole,
            summary.Capabilities,
            domains);
    }

    private static OrganizationMember MapMember(
        MemberReadRow row,
        IReadOnlyList<string> domains)
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
    }

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

    private sealed record OrganizationReadRow(
        Guid Id,
        string Name,
        string NormalizedName,
        string Slug,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string Role);

    private sealed record MemberReadRow(
        Guid Id,
        Guid UserId,
        string Name,
        string Email,
        string? ImageUrl,
        string Role,
        DateTimeOffset JoinedAt);
}
