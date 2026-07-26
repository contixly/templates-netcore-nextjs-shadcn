namespace Template.Domain.Authentication;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
}
