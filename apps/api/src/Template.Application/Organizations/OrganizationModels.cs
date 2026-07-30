using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Application.Organizations;

public sealed record OrganizationSummary(
    OrganizationId Id,
    string Name,
    OrganizationSlug Slug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OrganizationRole CurrentRole,
    OrganizationCapabilities Capabilities);

public sealed record OrganizationDetail(
    OrganizationId Id,
    string Name,
    OrganizationSlug Slug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OrganizationRole CurrentRole,
    OrganizationCapabilities Capabilities,
    IReadOnlyList<string> AllowedEmailDomains);

public sealed record OrganizationMember(
    OrganizationMemberId Id,
    UserId UserId,
    string Name,
    string Email,
    string? ImageUrl,
    OrganizationRole Role,
    DateTimeOffset JoinedAt,
    string? EmailDomain,
    bool IsOutsideAllowedEmailDomains);

public sealed record OrganizationDeletion(OrganizationId OrganizationId);

public sealed record ActiveOrganization(OrganizationId OrganizationId);

public sealed record OrganizationPage(
    IReadOnlyList<OrganizationSummary> Items,
    string? NextCursor);

public sealed record OrganizationMemberPage(
    IReadOnlyList<OrganizationMember> Items,
    string? NextCursor);

public enum OrganizationFailure
{
    InvalidName,
    InvalidSlug,
    InvalidEmailDomain,
    InvalidCursor,
    NotFound,
    PermissionDenied,
    NameConflict,
    SlugConflict,
    LastAccessibleOrganization,
    ConfirmationMismatch,
    TargetUserNotFound,
    MemberNotFound,
    MemberAlreadyExists,
    MemberRoleUnchanged,
    RoleAssignmentForbidden,
    DomainAcknowledgementRequired,
    OwnershipTransferRequired,
    ConcurrencyConflict
}

public sealed record OrganizationOperationResult<T>(
    T? Value,
    OrganizationFailure? Failure,
    OrganizationDomainAcknowledgement? Acknowledgement)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static OrganizationOperationResult<T> Success(T value) =>
        new(value, null, null);

    public static OrganizationOperationResult<T> Failed(
        OrganizationFailure failure,
        OrganizationDomainAcknowledgement? acknowledgement = null) =>
        new(null, failure, acknowledgement);
}

public sealed record OrganizationDomainAcknowledgement(
    string Email,
    string? EmailDomain,
    IReadOnlyList<string> AllowedEmailDomains);

public sealed record OrganizationCursorPosition(
    string NormalizedName,
    OrganizationId Id);

public sealed record OrganizationMemberCursorPosition(
    DateTimeOffset JoinedAt,
    OrganizationMemberId Id);

public sealed record OrganizationStorePage<TItem, TPosition>(
    IReadOnlyList<TItem> Items,
    TPosition? Next)
    where TItem : class
    where TPosition : class;

public sealed record CreateOrganizationCommand(
    UserId ActorUserId,
    SessionId SessionId,
    string Name);

public sealed record UpdateOrganizationCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    string? Name,
    OrganizationSlug? Slug,
    IReadOnlyList<string>? AllowedEmailDomains);

public sealed record DeleteOrganizationCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    string ConfirmationName);

public sealed record SetActiveOrganizationCommand(
    UserId ActorUserId,
    SessionId SessionId,
    OrganizationId OrganizationId);

public sealed record AddOrganizationMemberCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    UserId TargetUserId,
    OrganizationRole Role,
    bool AcknowledgeDomainRestriction);

public sealed record UpdateOrganizationMemberRoleCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    OrganizationMemberId MemberId,
    OrganizationRole Role);
