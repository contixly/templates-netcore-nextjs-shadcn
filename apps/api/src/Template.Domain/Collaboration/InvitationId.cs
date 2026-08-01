namespace Template.Domain.Collaboration;

public readonly record struct InvitationId(Guid Value)
{
    public static InvitationId New() => new(Guid.NewGuid());
}
