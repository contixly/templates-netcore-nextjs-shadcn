using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.ApiKeys;
using Template.Application.ApiKeys.Ports;
using Template.Application.Common.Ports;
using Template.Domain.ApiKeys;
using Template.Domain.Organizations;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.ApiKeys;

internal sealed class EfApiKeyStore(
    TemplateDbContext db,
    IApplicationUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IApiKeyStore
{
    private const int MaximumTransactionAttempts = 3;

    public Task<ApiKeyOperationResult<ApiKeyStorePage>> ListAsync(
        ApiKeyListQuery query,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async ct =>
        {
            var authorization = await LockAndAuthorizeOwnerAsync<ApiKeyStorePage>(query.ActorUserId.Value, query.Owner, ct);
            if (authorization is not null)
            {
                return authorization;
            }

            var keys = db.ApiKeys.AsNoTracking().Where(key => key.RevokedAt == null);
            keys = query.Owner.Kind == ApiKeyOwnerKind.User
                ? keys.Where(key => key.UserId == query.Owner.UserId!.Value.Value)
                : keys.Where(key => key.OrganizationId == query.Owner.OrganizationId!.Value.Value);
            if (query.After is not null)
            {
                keys = keys.Where(key =>
                    key.CreatedAt < query.After.CreatedAt ||
                    (key.CreatedAt == query.After.CreatedAt && key.Id.CompareTo(query.After.Id.Value) < 0));
            }

            var rows = await keys.OrderByDescending(key => key.CreatedAt)
                .ThenByDescending(key => key.Id)
                .Take(query.Limit + 1)
                .ToArrayAsync(ct);
            var hasMore = rows.Length > query.Limit;
            var pageRows = hasMore ? rows[..query.Limit] : rows;
            var next = hasMore
                ? new ApiKeyCursorPosition(pageRows[^1].CreatedAt, new ApiKeyId(pageRows[^1].Id))
                : null;
            return ApiKeyOperationResult<ApiKeyStorePage>.Success(new(pageRows.Select(Map).ToArray(), next));
        }, cancellationToken);

    public Task<ApiKeyOperationResult<ApiKeySummary>> CreateAsync(
        CreateApiKeyStoreCommand command,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async ct =>
        {
            var authorization = await LockAndAuthorizeOwnerAsync<ApiKeySummary>(command.ActorUserId.Value, command.Owner, ct);
            if (authorization is not null)
            {
                return authorization;
            }

            var id = ApiKeyId.New(command.CreatedAt);
            var entity = new ApiKeyEntity
            {
                Id = id.Value,
                UserId = command.Owner.UserId?.Value,
                OrganizationId = command.Owner.OrganizationId?.Value,
                Name = command.Name,
                KeyHash = command.Hash,
                KeyStart = command.Start,
                Scopes = command.Scopes.ToArray(),
                Enabled = true,
                RateLimitEnabled = command.RateLimitEnabled,
                RateLimitWindowSeconds = checked((int)command.RateLimitWindow.TotalSeconds),
                RateLimitMax = command.RateLimitMax,
                WindowStartedAt = null,
                RequestCount = 0,
                ExpiresAt = command.ExpiresAt,
                CreatedAt = command.CreatedAt,
                UpdatedAt = command.CreatedAt
            };
            db.ApiKeys.Add(entity);
            return ApiKeyOperationResult<ApiKeySummary>.Success(Map(entity));
        }, IsHashCollision, cancellationToken);

    public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(
        UpdateApiKeyCommand command,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async ct =>
        {
            var authorization = await LockAndAuthorizeOwnerAsync<ApiKeySummary>(command.ActorUserId.Value, command.Owner, ct);
            if (authorization is not null)
            {
                return authorization;
            }

            var entity = await LockOwnerQualifiedKeyAsync(command.Owner, command.ApiKeyId.Value, ct);
            if (entity is null || entity.RevokedAt is not null)
            {
                return ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
            }

            var changed = false;
            changed |= SetIfDifferent(command.Name, entity.Name, value => entity.Name = value);
            if (command.Scopes is not null && !command.Scopes.SequenceEqual(entity.Scopes, StringComparer.Ordinal))
            {
                entity.Scopes = command.Scopes.ToArray();
                changed = true;
            }
            if (command.ExpiresIn is not null && entity.ExpiresAt != command.ExpiresAt)
            {
                entity.ExpiresAt = command.ExpiresAt;
                changed = true;
            }
            changed |= SetIfDifferent(command.Enabled, entity.Enabled, value => entity.Enabled = value);

            var rateConfigurationChanged = false;
            rateConfigurationChanged |= SetIfDifferent(command.RateLimitEnabled, entity.RateLimitEnabled, value => entity.RateLimitEnabled = value);
            rateConfigurationChanged |= SetIfDifferent(command.RateLimitMax, entity.RateLimitMax, value => entity.RateLimitMax = value);
            if (command.RateLimitWindow is not null)
            {
                ApiKeyPolicy.TryGetRateLimitWindow(command.RateLimitWindow, out var window);
                rateConfigurationChanged |= SetIfDifferent(checked((int)window.TotalSeconds), entity.RateLimitWindowSeconds, value => entity.RateLimitWindowSeconds = value);
            }
            if (rateConfigurationChanged)
            {
                entity.WindowStartedAt = null;
                entity.RequestCount = 0;
                changed = true;
            }

            if (!changed)
            {
                return ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.Unchanged);
            }
            entity.UpdatedAt = timeProvider.GetUtcNow();
            return ApiKeyOperationResult<ApiKeySummary>.Success(Map(entity));
        }, cancellationToken);

    public Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(
        RevokeApiKeyCommand command,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async ct =>
        {
            var authorization = await LockAndAuthorizeOwnerAsync<ApiKeyRevocation>(command.ActorUserId.Value, command.Owner, ct);
            if (authorization is not null)
            {
                return authorization;
            }
            var entity = await LockOwnerQualifiedKeyAsync(command.Owner, command.ApiKeyId.Value, ct);
            if (entity is null || entity.RevokedAt is not null)
            {
                return ApiKeyOperationResult<ApiKeyRevocation>.Failed(ApiKeyFailure.NotFound);
            }
            var now = timeProvider.GetUtcNow();
            entity.RevokedAt = now;
            entity.UpdatedAt = now;
            return ApiKeyOperationResult<ApiKeyRevocation>.Success(new(command.ApiKeyId, now));
        }, cancellationToken);

    public Task<ApiKeyOperationResult<ApiKeySummary>> RotateAsync(
        RotateApiKeyStoreCommand command,
        CancellationToken cancellationToken) =>
        ExecuteWithRetryAsync(async ct =>
        {
            var authorization = await LockAndAuthorizeOwnerAsync<ApiKeySummary>(command.ActorUserId.Value, command.Owner, ct);
            if (authorization is not null)
            {
                return authorization;
            }
            var entity = await LockOwnerQualifiedKeyAsync(command.Owner, command.ApiKeyId.Value, ct);
            if (entity is null || entity.RevokedAt is not null)
            {
                return ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.NotFound);
            }
            entity.KeyHash = command.Hash;
            entity.KeyStart = command.Start;
            entity.RotatedAt = command.RotatedAt;
            entity.UpdatedAt = command.RotatedAt;
            entity.WindowStartedAt = null;
            entity.RequestCount = 0;
            return ApiKeyOperationResult<ApiKeySummary>.Success(Map(entity));
        }, IsHashCollision, cancellationToken);

    public Task<ApiKeyAuthenticationResult> AuthenticateAndConsumeAsync(
        byte[] hash,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ExecuteAuthenticationWithRetryAsync(async ct =>
        {
            var entity = await db.ApiKeys.FromSqlInterpolated(
                    $"SELECT * FROM auth.api_keys WHERE key_hash = {hash} FOR UPDATE")
                .SingleOrDefaultAsync(ct);
            if (entity is null || entity.RevokedAt is not null || !entity.Enabled || entity.ExpiresAt <= now)
            {
                return ApiKeyAuthenticationResult.Invalid();
            }

            var window = TimeSpan.FromSeconds(entity.RateLimitWindowSeconds);
            if (entity.WindowStartedAt is null || now >= entity.WindowStartedAt.Value + window)
            {
                entity.WindowStartedAt = now;
                entity.RequestCount = 0;
            }

            if (entity.RateLimitEnabled && entity.RequestCount >= entity.RateLimitMax)
            {
                var remaining = entity.WindowStartedAt.Value + window - now;
                var seconds = Math.Max(1, Math.Ceiling(remaining.TotalSeconds));
                return ApiKeyAuthenticationResult.RateLimited(TimeSpan.FromSeconds(seconds));
            }

            entity.RequestCount++;
            entity.LastRequestAt = now;
            var owner = entity.UserId is not null
                ? new ApiKeyOwner(ApiKeyOwnerKind.User, new(entity.UserId.Value), null)
                : new ApiKeyOwner(ApiKeyOwnerKind.Organization, null, new(entity.OrganizationId!.Value));
            return ApiKeyAuthenticationResult.Succeeded(new(new(entity.Id), entity.KeyStart, owner, entity.Scopes));
        }, cancellationToken);

    private async Task<ApiKeyOperationResult<T>?> LockAndAuthorizeOwnerAsync<T>(
        Guid actorUserId,
        ApiKeyOwner owner,
        CancellationToken cancellationToken) where T : class
    {
        if (owner.Kind == ApiKeyOwnerKind.User)
        {
            if (owner.UserId?.Value != actorUserId || owner.OrganizationId is not null)
            {
                return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.PermissionDenied);
            }
            var user = await db.Users.FromSqlInterpolated(
                    $"SELECT * FROM auth.users WHERE id = {actorUserId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            return user is null
                ? ApiKeyOperationResult<T>.Failed(ApiKeyFailure.NotFound)
                : null;
        }

        if (owner.Kind != ApiKeyOwnerKind.Organization || owner.OrganizationId is null || owner.UserId is not null)
        {
            return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.PermissionDenied);
        }
        var organizationId = owner.OrganizationId.Value.Value;
        var organization = await db.Organizations.FromSqlInterpolated(
                $"SELECT * FROM organizations.organizations WHERE id = {organizationId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (organization is null)
        {
            return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.NotFound);
        }
        var membership = await db.OrganizationMembers.FromSqlInterpolated(
                $"SELECT * FROM organizations.members WHERE organization_id = {organizationId} AND user_id = {actorUserId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (membership is null)
        {
            return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.NotFound);
        }
        return membership.Role is "owner" or "admin"
            ? null
            : ApiKeyOperationResult<T>.Failed(ApiKeyFailure.PermissionDenied);
    }

    private Task<ApiKeyEntity?> LockOwnerQualifiedKeyAsync(ApiKeyOwner owner, Guid keyId, CancellationToken cancellationToken) =>
        owner.Kind == ApiKeyOwnerKind.User
            ? db.ApiKeys.FromSqlInterpolated($"SELECT * FROM auth.api_keys WHERE id = {keyId} AND user_id = {owner.UserId!.Value.Value} FOR UPDATE").SingleOrDefaultAsync(cancellationToken)
            : db.ApiKeys.FromSqlInterpolated($"SELECT * FROM auth.api_keys WHERE id = {keyId} AND organization_id = {owner.OrganizationId!.Value.Value} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);

    private async Task<ApiKeyOperationResult<T>> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<ApiKeyOperationResult<T>>> action,
        CancellationToken cancellationToken) where T : class =>
        await ExecuteWithRetryAsync(action, _ => false, cancellationToken);

    private async Task<ApiKeyOperationResult<T>> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<ApiKeyOperationResult<T>>> action,
        Func<Exception, bool> collision,
        CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 1; attempt <= MaximumTransactionAttempts; attempt++)
        {
            try
            {
                if (db.Database.CurrentTransaction is null)
                {
                    db.ChangeTracker.Clear();
                }
                return await unitOfWork.ExecuteAsync(action, cancellationToken);
            }
            catch (Exception exception) when (collision(exception))
            {
                return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.ConcurrencyConflict);
            }
            catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
            {
                if (attempt == MaximumTransactionAttempts)
                {
                    return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.ConcurrencyConflict);
                }
            }
        }
        return ApiKeyOperationResult<T>.Failed(ApiKeyFailure.ConcurrencyConflict);
    }

    private async Task<ApiKeyAuthenticationResult> ExecuteAuthenticationWithRetryAsync(
        Func<CancellationToken, Task<ApiKeyAuthenticationResult>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaximumTransactionAttempts; attempt++)
        {
            try
            {
                if (db.Database.CurrentTransaction is null)
                {
                    db.ChangeTracker.Clear();
                }
                return await unitOfWork.ExecuteAsync(action, cancellationToken);
            }
            catch (Exception exception) when (IsTransactionConcurrencyFailure(exception))
            {
                if (attempt == MaximumTransactionAttempts)
                {
                    throw;
                }
            }
        }
        throw new InvalidOperationException("Unreachable API key authentication retry state.");
    }

    private static bool IsHashCollision(Exception exception) =>
        FindPostgresException(exception) is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_api_keys_key_hash" };

    private static bool IsTransactionConcurrencyFailure(Exception exception) =>
        FindPostgresException(exception)?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected;

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }
        }
        return null;
    }

    private static bool SetIfDifferent<T>(T? candidate, T current, Action<T> assign) where T : struct
    {
        if (candidate is null || EqualityComparer<T>.Default.Equals(candidate.Value, current)) return false;
        assign(candidate.Value);
        return true;
    }

    private static bool SetIfDifferent(string? candidate, string current, Action<string> assign)
    {
        if (candidate is null || string.Equals(candidate, current, StringComparison.Ordinal)) return false;
        assign(candidate);
        return true;
    }

    private static ApiKeySummary Map(ApiKeyEntity entity)
    {
        var owner = entity.UserId is not null
            ? new ApiKeyOwner(ApiKeyOwnerKind.User, new(entity.UserId.Value), null)
            : new ApiKeyOwner(ApiKeyOwnerKind.Organization, null, new(entity.OrganizationId!.Value));
        return new(
            new(entity.Id), owner, entity.Name, entity.KeyStart, entity.Scopes,
            entity.Enabled, entity.RateLimitEnabled, entity.RateLimitMax,
            TimeSpan.FromSeconds(entity.RateLimitWindowSeconds), entity.RequestCount,
            entity.WindowStartedAt, entity.LastRequestAt, entity.ExpiresAt,
            entity.RotatedAt, entity.CreatedAt, entity.UpdatedAt);
    }
}
