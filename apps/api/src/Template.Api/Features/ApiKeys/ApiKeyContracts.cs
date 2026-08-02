using System.Text.Json.Serialization;

namespace Template.Api.Features.ApiKeys;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CreateApiKeyRequest(
    string? Name,
    IReadOnlyList<string>? PresetIds,
    string? ExpiresIn,
    bool? RateLimitEnabled,
    int? RateLimitMax,
    string? RateLimitWindow);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UpdateApiKeyRequest(
    string? Name,
    IReadOnlyList<string>? PresetIds,
    string? ExpiresIn,
    bool? Enabled,
    bool? RateLimitEnabled,
    int? RateLimitMax,
    string? RateLimitWindow);

internal sealed record ApiKeyResponse(
    Guid Id,
    string OwnerKind,
    Guid OwnerId,
    string Name,
    string Start,
    string Status,
    bool Enabled,
    IReadOnlyList<string> Scopes,
    bool RateLimitEnabled,
    int RateLimitMax,
    string RateLimitWindow,
    int RequestCount,
    DateTimeOffset? WindowStartedAt,
    DateTimeOffset? LastRequestAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed class ApiKeySecretResponse(
    Guid id,
    string ownerKind,
    Guid ownerId,
    string name,
    string start,
    string status,
    bool enabled,
    IReadOnlyList<string> scopes,
    bool rateLimitEnabled,
    int rateLimitMax,
    string rateLimitWindow,
    int requestCount,
    DateTimeOffset? windowStartedAt,
    DateTimeOffset? lastRequestAt,
    DateTimeOffset? expiresAt,
    DateTimeOffset? rotatedAt,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt,
    string key)
{
    public Guid Id { get; } = id;
    public string OwnerKind { get; } = ownerKind;
    public Guid OwnerId { get; } = ownerId;
    public string Name { get; } = name;
    public string Start { get; } = start;
    public string Status { get; } = status;
    public bool Enabled { get; } = enabled;
    public IReadOnlyList<string> Scopes { get; } = scopes;
    public bool RateLimitEnabled { get; } = rateLimitEnabled;
    public int RateLimitMax { get; } = rateLimitMax;
    public string RateLimitWindow { get; } = rateLimitWindow;
    public int RequestCount { get; } = requestCount;
    public DateTimeOffset? WindowStartedAt { get; } = windowStartedAt;
    public DateTimeOffset? LastRequestAt { get; } = lastRequestAt;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public DateTimeOffset? RotatedAt { get; } = rotatedAt;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public DateTimeOffset UpdatedAt { get; } = updatedAt;
    public string Key { get; } = key;

    public override string ToString() =>
        $"{nameof(ApiKeySecretResponse)} {{ Id = {Id}, OwnerKind = {OwnerKind}, OwnerId = {OwnerId}, Key = [REDACTED] }}";
}

internal sealed record ApiKeyPageResponse(
    IReadOnlyList<ApiKeyResponse> Items,
    string? NextCursor);

internal sealed record ApiKeyRevocationResponse(Guid Id, DateTimeOffset RevokedAt);

internal sealed record ApiKeyMeResponse(
    ApiKeyMePrincipalResponse Principal,
    ApiKeyMeKeyResponse Key,
    IReadOnlyList<string> Scopes);

internal sealed record ApiKeyMePrincipalResponse(
    string OwnerKind,
    Guid? UserId,
    Guid? OrganizationId);

internal sealed record ApiKeyMeKeyResponse(
    Guid Id,
    string Start,
    string ConfigId);
