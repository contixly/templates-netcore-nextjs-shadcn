namespace Template.Domain.Authentication;

public sealed record SessionWindow(
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt)
{
    public static SessionWindow Start(DateTimeOffset now, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                "Session lifetime must be positive.");
        }

        var utcNow = now.ToUniversalTime();
        return new SessionWindow(utcNow, utcNow, utcNow.Add(lifetime));
    }

    public bool IsExpired(DateTimeOffset now) =>
        ExpiresAt <= now.ToUniversalTime();
}
