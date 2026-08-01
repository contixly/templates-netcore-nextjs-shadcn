namespace Template.Domain.Collaboration;

public static class InvitationPolicy
{
    public static InvitationDisplayState GetDisplayState(
        InvitationStatus status,
        DateTimeOffset expiresAt,
        DateTimeOffset now) =>
        status == InvitationStatus.Pending && expiresAt <= now
            ? InvitationDisplayState.Expired
            : status switch
            {
                var value when value == InvitationStatus.Pending => InvitationDisplayState.Pending,
                var value when value == InvitationStatus.Accepted => InvitationDisplayState.Accepted,
                var value when value == InvitationStatus.Rejected => InvitationDisplayState.Rejected,
                var value when value == InvitationStatus.Canceled => InvitationDisplayState.Canceled,
                _ => default
            };

    public static bool CanAccept(
        InvitationStatus status,
        DateTimeOffset expiresAt,
        DateTimeOffset now) =>
        GetDisplayState(status, expiresAt, now) == InvitationDisplayState.Pending;

    public static bool CanReject(
        InvitationStatus status,
        DateTimeOffset expiresAt,
        DateTimeOffset now) =>
        GetDisplayState(status, expiresAt, now) == InvitationDisplayState.Pending;
}
