namespace Template.Domain.Collaboration;

public readonly record struct TeamMemberId(Guid Value)
{
    public static TeamMemberId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));
}
