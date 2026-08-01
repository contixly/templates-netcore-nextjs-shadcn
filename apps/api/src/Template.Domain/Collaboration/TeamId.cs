namespace Template.Domain.Collaboration;

public readonly record struct TeamId(Guid Value)
{
    public static TeamId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));
}
