using System.Text.Json.Serialization;

namespace Template.Api.Features.Collaboration;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CreateInvitationRequest
{
    public string? Email { get; init; }

    public string? Role { get; init; }

    public string? TeamId { get; init; }
}

internal sealed record InvitationResponse(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string CanonicalOrganizationKey,
    Guid? TeamId,
    string? TeamName,
    string Email,
    string Role,
    string Status,
    string DisplayState,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    Guid InviterId,
    string InviterName,
    string InvitationPath);

internal sealed record OrganizationInvitationPageResponse(
    IReadOnlyList<InvitationResponse> Items,
    string? NextCursor);

internal sealed record AccountInvitationPageResponse(
    IReadOnlyList<InvitationResponse> Items,
    string? NextCursor);

internal sealed record InvitationDecisionResponse(
    InvitationResponse? Invitation,
    string State,
    bool CanRespond);

internal sealed record AcceptedInvitationResponse(
    Guid InvitationId,
    Guid OrganizationId,
    string CanonicalOrganizationKey);
