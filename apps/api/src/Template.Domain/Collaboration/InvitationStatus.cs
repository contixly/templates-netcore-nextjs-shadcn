namespace Template.Domain.Collaboration;

public readonly record struct InvitationStatus
{
    public static InvitationStatus Pending { get; } = new("pending");
    public static InvitationStatus Accepted { get; } = new("accepted");
    public static InvitationStatus Rejected { get; } = new("rejected");
    public static InvitationStatus Canceled { get; } = new("canceled");

    public string Value { get; }

    private InvitationStatus(string value) => Value = value;

    public static bool TryParse(string? value, out InvitationStatus status)
    {
        status = value switch
        {
            "pending" => Pending,
            "accepted" => Accepted,
            "rejected" => Rejected,
            "canceled" => Canceled,
            _ => default
        };
        return value is "pending" or "accepted" or "rejected" or "canceled";
    }

    public override string ToString() => Value;
}

public readonly record struct InvitationDisplayState
{
    public static InvitationDisplayState Pending { get; } = new("pending");
    public static InvitationDisplayState Accepted { get; } = new("accepted");
    public static InvitationDisplayState Rejected { get; } = new("rejected");
    public static InvitationDisplayState Canceled { get; } = new("canceled");
    public static InvitationDisplayState Expired { get; } = new("expired");

    public string Value { get; }

    private InvitationDisplayState(string value) => Value = value;

    public override string ToString() => Value;
}
