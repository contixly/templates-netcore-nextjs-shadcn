using Template.Domain.Authentication;

namespace Template.Application.Tests;

public sealed class SessionWindowTests
{
    [Fact]
    public void StartNormalizesUtcAndCreatesExpectedExpiry()
    {
        var local = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(3));

        var window = SessionWindow.Start(local, TimeSpan.FromDays(7));

        Assert.Equal(TimeSpan.Zero, window.CreatedAt.Offset);
        Assert.Equal(window.CreatedAt, window.UpdatedAt);
        Assert.Equal(window.CreatedAt.AddDays(7), window.ExpiresAt);
    }

    [Fact]
    public void StartRejectsNonPositiveLifetime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SessionWindow.Start(DateTimeOffset.UtcNow, TimeSpan.Zero));
    }

    [Fact]
    public void NewIdentifiersAreVersionSevenAndDistinct()
    {
        var user = UserId.New();
        var session = SessionId.New();

        Assert.Equal(7, user.Value.Version);
        Assert.Equal(7, session.Value.Version);
        Assert.NotEqual(user.Value, session.Value);
    }
}
