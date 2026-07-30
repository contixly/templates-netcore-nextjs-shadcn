using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Template.Api.Features.Account;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record UpdateProfileRequest(
    [property: Required] string? DisplayName);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record DeleteAccountRequest(
    [property: Required] string? ConfirmationEmail);

internal sealed record AccountEmailResponse(
    string Email,
    bool IsPrimary,
    IReadOnlyList<string> Providers);

internal sealed record AccountResponse(
    Guid Id,
    string DisplayName,
    string PrimaryEmail,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AccountEmailResponse> VerifiedEmails);

internal sealed record AccountConnectionsResponse(
    IReadOnlyList<AccountConnectionResponse> Items);

internal sealed record AccountConnectionResponse(
    string Provider,
    string DisplayName,
    bool Configured,
    bool Connected,
    string? Email,
    DateTimeOffset? ConnectedAt,
    DateTimeOffset? LastUsedAt,
    bool IsCurrentAuthenticationMethod,
    bool CanConnect,
    bool CanDisconnect,
    string? DisabledReason);

internal sealed record AccountDisconnectionResponse(string Provider);

internal sealed record AccountSessionsResponse(
    IReadOnlyList<AccountSessionResponse> Items,
    string? NextCursor);

internal sealed record AccountSessionResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent,
    string AuthenticationMethod,
    string? IpAddress,
    string? UserAgent);

internal sealed record AccountSessionRevocationResponse(Guid SessionId);

internal sealed record AccountSessionsRevocationResponse(int RevokedCount);

internal sealed record AccountDeletionResponse(bool Deleted);
