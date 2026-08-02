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

internal sealed record ApiKeySecretResponse(
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
    DateTimeOffset UpdatedAt,
    string Key);

internal sealed record ApiKeyPageResponse(
    IReadOnlyList<ApiKeyResponse> Items,
    string? NextCursor);

internal sealed record ApiKeyRevocationResponse(Guid Id, DateTimeOffset RevokedAt);
