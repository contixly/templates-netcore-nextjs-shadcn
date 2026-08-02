using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;

namespace Template.Application.ApiKeys;

public sealed class ApiKeyManagementService(
    IApiKeyStore store,
    IApiKeyCredentialService credentials)
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

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var material = credentials.Generate(command.OwnerKind);
            var result = await store.CreateAsync(new(
                command.ActorUserId,
                owner,
                name,
                ApiKeyPolicy.ExpandPresets(command.PresetIds),
                new ApiKeyExpiration(expiration),
                command.RateLimitEnabled,
                command.RateLimitMax,
                rateLimitWindow,
                material.Hash,
                material.Start), cancellationToken);
            if (result.Succeeded)
            {
                return ApiKeyOperationResult<ApiKeySecret>.Success(new(RequireValue(result), material.Credential));
            }
            if (RequireFailure(result) != ApiKeyFailure.ConcurrencyConflict || attempt == 3)
            {
                return ApiKeyOperationResult<ApiKeySecret>.Failed(RequireFailure(result));
            }
        }
        throw new InvalidOperationException("Unreachable API key create retry state.");
    }

    public Task<ApiKeyOperationResult<ApiKeySummary>> UpdateAsync(UpdateApiKeyCommand command, CancellationToken cancellationToken)
    {
        string? name = null;
        TimeSpan? expiration = null;
        TimeSpan? rateLimitWindow = null;
        if (!TryGetOwner(command.ActorUserId, command.OwnerKind, command.OrganizationId, out var owner))
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
        if (command.RateLimitWindow is not null)
        {
            if (!ApiKeyPolicy.TryGetRateLimitWindow(command.RateLimitWindow, out var parsedRateLimitWindow))
            {
                return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.InvalidRateLimit));
            }

            rateLimitWindow = parsedRateLimitWindow;
        }
        if (command.RateLimitMax is not null && !ApiKeyPolicy.IsValidRateLimitMax(command.RateLimitMax.Value))
        {
            return Task.FromResult(ApiKeyOperationResult<ApiKeySummary>.Failed(ApiKeyFailure.InvalidRateLimit));
        }

        return store.UpdateAsync(
            new UpdateApiKeyStoreCommand(
                command.ActorUserId,
                owner,
                command.ApiKeyId,
                command.Name is null ? null : name,
                command.PresetIds is null
                    ? null
                    : ApiKeyPolicy.ExpandPresets(command.PresetIds),
                command.ExpiresIn is null
                    ? null
                    : new ApiKeyExpiration(expiration),
                command.Enabled,
                command.RateLimitEnabled,
                command.RateLimitMax,
                rateLimitWindow),
            cancellationToken);
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

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var material = credentials.Generate(command.OwnerKind);
            var result = await store.RotateAsync(
                new(
                    command.ActorUserId,
                    owner,
                    command.ApiKeyId,
                    material.Hash,
                    material.Start),
                cancellationToken);
            if (result.Succeeded)
            {
                return ApiKeyOperationResult<ApiKeySecret>.Success(new(RequireValue(result), material.Credential));
            }
            if (RequireFailure(result) != ApiKeyFailure.ConcurrencyConflict || attempt == 3)
            {
                return ApiKeyOperationResult<ApiKeySecret>.Failed(RequireFailure(result));
            }
        }
        throw new InvalidOperationException("Unreachable API key rotation retry state.");
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
