using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Collaboration;

public sealed record InvitationView(
    InvitationId Id,
    OrganizationId OrganizationId,
    string OrganizationName,
    string CanonicalOrganizationKey,
    TeamId? TeamId,
    string? TeamName,
    string Email,
    OrganizationRole Role,
    InvitationStatus Status,
    InvitationDisplayState DisplayState,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    UserId InviterId,
    string InviterName);

public sealed record InvitationDecision(
    InvitationView? Invitation,
    InvitationDecisionState State,
    bool CanRespond);

public sealed record AcceptedInvitation(
    InvitationId InvitationId,
    OrganizationId OrganizationId,
    string CanonicalOrganizationKey);

public sealed record InvitationNotification(string RecipientEmail, string InvitationPath);

public sealed record InvitationActor(
    UserId UserId,
    string NormalizedPrimaryEmail,
    bool IsEmailVerified);

public static class InvitationWarnings
{
    public const string NotificationFailed = "notification_failed";
}

public enum InvitationNotificationOutcome
{
    Completed,
    Skipped,
    Failed
}

public readonly record struct InvitationDecisionState
{
    public static InvitationDecisionState Pending { get; } = new("pending");
    public static InvitationDecisionState Accepted { get; } = new("accepted");
    public static InvitationDecisionState Rejected { get; } = new("rejected");
    public static InvitationDecisionState Canceled { get; } = new("canceled");
    public static InvitationDecisionState Expired { get; } = new("expired");
    public static InvitationDecisionState RecipientMismatch { get; } = new("recipient-mismatch");
    public static InvitationDecisionState EmailVerificationRequired { get; } = new("email-verification-required");
    public static InvitationDecisionState DomainRestricted { get; } = new("domain-restricted");
    public static InvitationDecisionState AlreadyMember { get; } = new("already-member");

    public string Value { get; }

    private InvitationDecisionState(string value) => Value = value;

    public override string ToString() => Value;
}

public sealed record OrganizationInvitationPage(
    IReadOnlyList<InvitationView> Items,
    string? NextCursor);

public sealed record AccountInvitationPage(
    IReadOnlyList<InvitationView> Items,
    string? NextCursor);

public enum InvitationFailure
{
    InvalidCursor,
    NotFound,
    PermissionDenied,
    AlreadyExists,
    RecipientAlreadyMember,
    TeamInvalid,
    DomainRestricted,
    RecipientMismatch,
    EmailVerificationRequired,
    Expired,
    NotPending,
    MembershipConflict,
    LimitReached,
    ConcurrencyConflict
}

public sealed record InvitationOperationResult<T>(T? Value, InvitationFailure? Failure, string? Warning = null)
    where T : class
{
    public bool Succeeded => Failure is null;

    public static InvitationOperationResult<T> Success(T value) => new(value, null);

    public static InvitationOperationResult<T> Failed(InvitationFailure failure) => new(null, failure);
}

public sealed record InvitationStorePage<TItem, TPosition>(
    IReadOnlyList<TItem> Items,
    TPosition? Next)
    where TItem : class
    where TPosition : class;

public sealed record OrganizationInvitationCursorPosition(DateTimeOffset CreatedAt, InvitationId Id);

public sealed record AccountInvitationCursorPosition(
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    InvitationId Id);

public sealed record CreateInvitationCommand(
    UserId ActorUserId,
    OrganizationId OrganizationId,
    string Email,
    OrganizationRole Role,
    TeamId? TeamId);

public sealed record AcceptInvitationCommand(
    InvitationActor Actor,
    SessionId SessionId,
    InvitationId InvitationId);

public sealed record RejectInvitationCommand(InvitationActor Actor, InvitationId InvitationId);
