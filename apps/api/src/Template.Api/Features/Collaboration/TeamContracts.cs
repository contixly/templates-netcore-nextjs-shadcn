using System.Text.Json.Serialization;

namespace Template.Api.Features.Collaboration;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record TeamNameRequest
{
    public string? Name { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AddTeamMemberRequest
{
    public string? UserId { get; init; }
}

internal sealed record TeamResponse(
    Guid Id,
    Guid OrganizationId,
    string Name,
    int MemberCount,
    TeamMemberPageResponse Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record TeamPageResponse(
    IReadOnlyList<TeamResponse> Items,
    string? NextCursor);

internal sealed record TeamMemberResponse(
    Guid Id,
    Guid UserId,
    string Name,
    string Email,
    string? ImageUrl,
    string Role,
    DateTimeOffset OrganizationJoinedAt,
    DateTimeOffset TeamJoinedAt);

internal sealed record TeamMemberPageResponse(
    IReadOnlyList<TeamMemberResponse> Items,
    string? NextCursor);

internal sealed record TeamCandidateResponse(
    Guid MemberId,
    Guid UserId,
    string Name,
    string Email,
    string? ImageUrl,
    string Role,
    DateTimeOffset JoinedAt);

internal sealed record TeamCandidatePageResponse(
    IReadOnlyList<TeamCandidateResponse> Items,
    string? NextCursor);

internal sealed record TeamDeletionResponse(Guid TeamId);

internal sealed record TeamMemberRemovalResponse(Guid TeamId, Guid UserId);
