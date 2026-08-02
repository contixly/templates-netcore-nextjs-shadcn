using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;

namespace Template.Application.ApiKeys;

public sealed class ApiKeyManagementService(
    IApiKeyStore store,
    IApiKeyCredentialService credentials,
    TimeProvider timeProvider)
{
    public async Task<ApiKeyOperationResult<ApiKeyPage>> ListAsync(ApiKeyListRequest request, CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "API key page limit must be between 1 and 100.");
        }

        if (!TryGetOwner(request.ActorUserId, request.OwnerKind, request.OrganizationId, out var owner))
        {
            return ApiKeyOperationResult<ApiKeyPage>.Failed(ApiKeyFailure.PermissionDenied);
        }

        ApiKeyCursorPosition? after = null;
        if (request.Cursor is not null && !ApiKeyCursor.TryDecode(request.Cursor, out after))
        {
            return ApiKeyOperationResult<ApiKeyPage>.Failed(ApiKeyFailure.InvalidCursor);
        }

        var result = await store.ListAsync(new(request.ActorUserId, owner, after, request.Limit), cancellationToken);
        if (!result.Succeeded)
        {
            return ApiKeyOperationResult<ApiKeyPage>.Failed(RequireFailure(result));
        }

        var page = RequireValue(result);
        return ApiKeyOperationResult<ApiKeyPage>.Success(new(page.Items, page.Next is null ? null : ApiKeyCursor.Encode(page.Next)));
    }

    public async Task<ApiKeyOperationResult<ApiKeySecret>> CreateAsync(CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        if (!TryGetOwner(command.ActorUserId, command.OwnerKind, command.OrganizationId, out var owner))
        {
            return ApiKeyOperationResult<ApiKeySecret>.Failed(ApiKeyFailure.PermissionDenied);
        }
        if (!ApiKeyPolicy.TryNormalizeName(command.Name, out var name))
        {
            return ApiKeyOperationResult<ApiKeySecret>.Failed(ApiKeyFailure.InvalidName);
        }
        if (!ApiKeyPolicy.AreValidPresets(command.PresetIds))
        {
            return ApiKeyOperationResult<ApiKeySecret>.Failed(ApiKeyFailure.InvalidPreset);
        }
        if (!ApiKeyPolicy.TryGetExpiration(command.ExpiresIn, out var expiration))
        {
            return ApiKeyOperationResult<ApiKeySecret>.Failed(ApiKeyFailure.InvalidExpiration);
        }
        if (!ApiKeyPolicy.TryGetRateLimitWindow(command.RateLimitWindow, out var rateLimitWindow)
            || !ApiKeyPolicy.IsValidRateLimitMax(command.RateLimitMax))
        {
            return ApiKeyOperationResult<ApiKeySecret>.Failed(ApiKeyFailure.InvalidRateLimit);
        }

        var now = timeProvider.GetUtcNow();
        var material = credentials.Generate(command.OwnerKind);
        var result = await store.CreateAsync(new(
            command.ActorUserId,
            owner,
            name,
            ApiKeyPolicy.ExpandPresets(command.PresetIds),
            expiration is null ? null : now + expiration.Value,
            command.RateLimitEnabled,
            command.RateLimitMax,
            rateLimitWindow,
            material.Hash,
            material.Start,
            now), cancellationToken);
        return result.Succeeded
            ? ApiKeyOperationResult<ApiKeySecret>.Success(new(RequireValue(result), material.Credential))
            : ApiKeyOperationResult<ApiKeySecret>.Failed(RequireFailure(result));
    }

    public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(UpdateApiKeyCommand command, CancellationToken cancellationToken)
    {
        string? name = null;
        TimeSpan? expiration = null;
        if (!TryGetOwner(command.ActorUserId, command.OwnerKind, command.OrganizationId, out _))
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.PermissionDenied));
        }
        if (command.Name is null && command.PresetIds is null && command.ExpiresIn is null && command.Enabled is null
            && command.RateLimitEnabled is null && command.RateLimitMax is null && command.RateLimitWindow is null)
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.Unchanged));
        }
        if (command.Name is not null && !ApiKeyPolicy.TryNormalizeName(command.Name, out name))
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.InvalidName));
        }
        if (command.PresetIds is not null && !ApiKeyPolicy.AreValidPresets(command.PresetIds))
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.InvalidPreset));
        }
        if (command.ExpiresIn is not null && !ApiKeyPolicy.TryGetExpiration(command.ExpiresIn, out expiration))
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.InvalidExpiration));
        }
        if ((command.RateLimitWindow is not null && !ApiKeyPolicy.TryGetRateLimitWindow(command.RateLimitWindow, out _))
            || (command.RateLimitMax is not null && !ApiKeyPolicy.IsValidRateLimitMax(command.RateLimitMax.Value)))
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.InvalidRateLimit));
        }

        var normalized = command with
        {
            Name = command.Name is null ? null : name,
            Scopes = command.PresetIds is null ? null : ApiKeyPolicy.ExpandPresets(command.PresetIds),
            ExpiresAt = command.ExpiresIn is null ? null : expiration is null ? null : timeProvider.GetUtcNow() + expiration.Value
        };
        return store.UpdateAsync(normalized, cancellationToken);
    }

    public Task<ApiKeyOperationResult<ApiKeyRevocation>> RevokeAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken) =>
        TryGetOwner(command.ActorUserId, command.OwnerKind, command.OrganizationId, out _)
            ? store.RevokeAsync(command, cancellationToken)
            : Task.FromResult(ApiKeyOperationResult<ApiKeyRevocation>.Failed(ApiKeyFailure.PermissionDenied));

    public async Task<ApiKeyOperationResult<ApiKeySecret>> RotateAsync(RotateApiKeyCommand command, CancellationToken cancellationToken)
    {
        if (!TryGetOwner(command.ActorUserId, command.OwnerKind, command.OrganizationId, out var owner))
        {
            return ApiKeyOperationResult<ApiKeySecret>.Failed(ApiKeyFailure.PermissionDenied);
        }

        var material = credentials.Generate(command.OwnerKind);
        var result = await store.RotateAsync(new(command.ActorUserId, owner, command.ApiKeyId, material.Hash, material.Start, timeProvider.GetUtcNow()), cancellationToken);
        return result.Succeeded
            ? ApiKeyOperationResult<ApiKeySecret>.Success(new(RequireValue(result), material.Credential))
            : ApiKeyOperationResult<ApiKeySecret>.Failed(RequireFailure(result));
    }

    private static bool TryGetOwner(Template.Domain.Authentication.UserId actorUserId, ApiKeyOwnerKind ownerKind, Template.Domain.Organizations.OrganizationId? organizationId, out ApiKeyOwner owner)
    {
        owner = ownerKind switch
        {
            ApiKeyOwnerKind.User => new(ApiKeyOwnerKind.User, actorUserId, null),
            ApiKeyOwnerKind.Organization when organizationId is not null => new(ApiKeyOwnerKind.Organization, null, organizationId),
            _ => default!
        };
        return owner is not null;
    }

    private static ApiKeyFailure RequireFailure<T>(ApiKeyOperationResult<T> result) where T : class =>
        result.Failure ?? throw new InvalidOperationException("A failed API key result requires a failure.");
    private static T RequireValue<T>(ApiKeyOperationResult<T> result) where T : class =>
        result.Value ?? throw new InvalidOperationException("A successful API key result requires a value.");
}
