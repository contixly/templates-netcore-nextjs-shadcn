using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Collaboration;

public sealed record TeamMemberView(
    TeamMemberId Id,
    UserId UserId,
    string Name,
    string Email,
    string? ImageUrl,
    OrganizationRole Role,
    DateTimeOffset OrganizationJoinedAt,
    DateTimeOffset TeamJoinedAt);

public sealed record TeamCandidate(
    OrganizationMemberId MemberId,
    UserId UserId,
    string Name,
    string Email,
    string? ImageUrl,
    OrganizationRole Role,
    DateTimeOffset JoinedAt);

public sealed record TeamSummary(
    TeamId Id,
    OrganizationId OrganizationId,
    TeamName Name,
    int MemberCount,
    TeamMemberPage Members,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TeamPage(IReadOnlyList<TeamSummary> Items, string? NextCursor);

public sealed record TeamMemberPage(
    IReadOnlyList<TeamMemberView> Items,
    string? NextCursor);

public sealed record TeamCandidatePage(
    IReadOnlyList<TeamCandidate> Items,
    string? NextCursor);

public sealed record TeamDeletion(TeamId TeamId);

public sealed record TeamMemberRemoval(TeamId TeamId, UserId UserId);

public enum TeamFailure
{
    InvalidName,
    InvalidCursor,
    NotFound,
    PermissionDenied,
    NameConflict,
    NameUnchanged,
    MemberNotFound,
    MemberAlreadyExists,
    ConcurrencyConflict
}

public sealed record TeamOperationResult<T>(T? Value, TeamFailure? Failure)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static TeamOperationResult<T> Success(T value) => new(value, null);

    public static TeamOperationResult<T> Failed(TeamFailure failure) => new(null, failure);
}

public sealed record TeamStorePage<TItem, TPosition>(
    IReadOnlyList<TItem> Items,
    TPosition? Next)
    where TItem : class
    where TPosition : class;

public sealed record TeamCursorPosition(DateTimeOffset CreatedAt, TeamId Id);

public sealed record TeamMemberCursorPosition(DateTimeOffset JoinedAt, TeamMemberId Id);

public sealed record TeamCandidateCursorPosition(
    DateTimeOffset JoinedAt,
    OrganizationMemberId Id);

public sealed record CreateTeamCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    TeamName Name);

public sealed record UpdateTeamCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    TeamId TeamId,
    TeamName Name);

public sealed record DeleteTeamCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    TeamId TeamId);

public sealed record AddTeamMemberCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    TeamId TeamId,
    UserId TargetUserId);

public sealed record RemoveTeamMemberCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    TeamId TeamId,
    UserId TargetUserId);
