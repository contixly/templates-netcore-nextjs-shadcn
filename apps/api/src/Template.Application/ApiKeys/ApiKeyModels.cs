using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.ApiKeys;

public sealed record ApiKeyOwner(ApiKeyOwnerKind Kind, UserId? UserId, OrganizationId? OrganizationId);

public sealed record ApiKeySummary(
    ApiKeyId Id,
    ApiKeyOwner Owner,
    string Name,
    string Start,
    IReadOnlyList<string> Scopes,
    bool Enabled,
    bool RateLimitEnabled,
    int RateLimitMax,
    TimeSpan RateLimitWindow,
    int RequestCount,
    DateTimeOffset? WindowStartedAt,
    DateTimeOffset? LastRequestAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ApiKeySecret(ApiKeySummary ApiKey, string Credential);
public sealed record ApiKeyRevocation(ApiKeyId Id, DateTimeOffset RevokedAt);
public sealed record ApiKeyPage(IReadOnlyList<ApiKeySummary> Items, string? NextCursor);
public sealed record ApiKeyStorePage(IReadOnlyList<ApiKeySummary> Items, ApiKeyCursorPosition? Next);
public sealed record ApiKeyCursorPosition(DateTimeOffset CreatedAt, ApiKeyId Id);

public enum ApiKeyFailure
{
    InvalidName,
    InvalidPreset,
    InvalidExpiration,
    InvalidRateLimit,
    InvalidCursor,
    PermissionDenied,
    NotFound,
    Unchanged,
    ConcurrencyConflict
}

public sealed record ApiKeyOperationResult<T>(T? Value, ApiKeyFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;
    public static ApiKeyOperationResult<T> Success(T value) => new(value, null);
    public static ApiKeyOperationResult<T> Failed(ApiKeyFailure failure) => new(null, failure);
}

public sealed record ApiKeyListRequest(
    UserId ActorUserId,
    ApiKeyOwnerKind OwnerKind,
    OrganizationId? OrganizationId,
    string? Cursor,
    int Limit);

public sealed record ApiKeyListQuery(
    UserId ActorUserId,
    ApiKeyOwner Owner,
    ApiKeyCursorPosition? After,
    int Limit);

public sealed record CreateApiKeyCommand(
    UserId ActorUserId,
    ApiKeyOwnerKind OwnerKind,
    OrganizationId? OrganizationId,
    string? Name,
    IReadOnlyList<string>? PresetIds,
    string? ExpiresIn,
    bool RateLimitEnabled,
    int RateLimitMax,
    string? RateLimitWindow);

public sealed record CreateApiKeyStoreCommand(
    UserId ActorUserId,
    ApiKeyOwner Owner,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    bool RateLimitEnabled,
    int RateLimitMax,
    TimeSpan RateLimitWindow,
    byte[] Hash,
    string Start,
    DateTimeOffset CreatedAt);

public sealed record UpdateApiKeyCommand(
    UserId ActorUserId,
    ApiKeyOwnerKind OwnerKind,
    OrganizationId? OrganizationId,
    ApiKeyId ApiKeyId,
    string? Name,
    IReadOnlyList<string>? PresetIds,
    string? ExpiresIn,
    bool? Enabled,
    bool? RateLimitEnabled,
    int? RateLimitMax,
    string? RateLimitWindow)
{
    public ApiKeyOwner Owner => OwnerKind == ApiKeyOwnerKind.User
        ? new(ApiKeyOwnerKind.User, ActorUserId, null)
        : new(ApiKeyOwnerKind.Organization, null, OrganizationId);

    public IReadOnlyList<string>? Scopes { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record RevokeApiKeyCommand(
    UserId ActorUserId,
    ApiKeyOwnerKind OwnerKind,
    OrganizationId? OrganizationId,
    ApiKeyId ApiKeyId)
{
    public ApiKeyOwner Owner => OwnerKind == ApiKeyOwnerKind.User
        ? new(ApiKeyOwnerKind.User, ActorUserId, null)
        : new(ApiKeyOwnerKind.Organization, null, OrganizationId);
}

public sealed record RotateApiKeyCommand(
    UserId ActorUserId,
    ApiKeyOwnerKind OwnerKind,
    OrganizationId? OrganizationId,
    ApiKeyId ApiKeyId)
{
    public ApiKeyOwner Owner => OwnerKind == ApiKeyOwnerKind.User
        ? new(ApiKeyOwnerKind.User, ActorUserId, null)
        : new(ApiKeyOwnerKind.Organization, null, OrganizationId);
}

public sealed record RotateApiKeyStoreCommand(
    UserId ActorUserId,
    ApiKeyOwner Owner,
    ApiKeyId ApiKeyId,
    byte[] Hash,
    string Start,
    DateTimeOffset RotatedAt);

public enum ApiKeyAuthenticationOutcome { Succeeded, Invalid, RateLimited }

public sealed record ApiKeyPrincipal(
    ApiKeyId Id,
    string Start,
    ApiKeyOwner Owner,
    IReadOnlyList<string> Scopes);

public sealed record ApiKeyAuthenticationResult(
    ApiKeyAuthenticationOutcome Outcome,
    ApiKeyPrincipal? Principal,
    TimeSpan? RetryAfter)
{
    public static ApiKeyAuthenticationResult Succeeded(ApiKeyPrincipal principal) => new(ApiKeyAuthenticationOutcome.Succeeded, principal, null);
    public static ApiKeyAuthenticationResult Invalid() => new(ApiKeyAuthenticationOutcome.Invalid, null, null);
    public static ApiKeyAuthenticationResult RateLimited(TimeSpan retryAfter) => new(ApiKeyAuthenticationOutcome.RateLimited, null, retryAfter);
}
