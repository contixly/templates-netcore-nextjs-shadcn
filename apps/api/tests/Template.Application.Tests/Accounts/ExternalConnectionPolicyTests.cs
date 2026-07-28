using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Tests.Accounts;

public sealed class ExternalConnectionPolicyTests
{
    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    [InlineData("gitlab")]
    [InlineData("vk")]
    [InlineData("yandex")]
    public void ProviderIdsAreClosedAndCanonical(string value) =>
        Assert.True(ExternalProvider.TryParse(value, out _));

    [Theory]
    [InlineData("Google")]
    [InlineData("GOOGLE")]
    [InlineData("microsoft")]
    [InlineData("")]
    public void UnknownOrNonCanonicalProviderIdsAreRejected(string value)
    {
        Assert.False(ExternalProvider.TryParse(value, out var provider));
        Assert.Equal(default, provider);
    }

    [Fact]
    public void ProvidersCannotBeConstructedOutsideTheClosedSet() =>
        Assert.Null(typeof(ExternalProvider).GetConstructor([typeof(string)]));

    [Theory]
    [InlineData(" Example@Example.com ", "Example@Example.com", "EXAMPLE@EXAMPLE.COM")]
    [InlineData("Example@Example.com", "Example@Example.com", "EXAMPLE@EXAMPLE.COM")]
    public void VerifiedEmailTrimsAndNormalizesInvariantly(
        string value,
        string expectedValue,
        string expectedNormalizedValue)
    {
        var email = VerifiedEmail.Create(value);

        Assert.Equal(expectedValue, email.Value);
        Assert.Equal(expectedNormalizedValue, email.NormalizedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("email\n@example.com")]
    public void VerifiedEmailRejectsEmptyOrControlContainingValues(string value) =>
        Assert.Throws<ArgumentException>(() => VerifiedEmail.Create(value));

    [Fact]
    public void VerifiedEmailRejectsValuesLongerThan254Characters() =>
        Assert.Throws<ArgumentException>(() => VerifiedEmail.Create(new string('a', 255)));

    [Fact]
    public void DifferentFreeEmailCanBeAttachedAsSecondary() =>
        Assert.Equal(
            EmailOwnershipDecision.AttachSecondary,
            ExternalConnectionPolicy.DecideEmailOwnership(new UserId(Guid.NewGuid()), null));

    [Fact]
    public void EmailOwnedByTheCurrentUserIsReused()
    {
        var userId = new UserId(Guid.NewGuid());

        Assert.Equal(
            EmailOwnershipDecision.ReuseCurrent,
            ExternalConnectionPolicy.DecideEmailOwnership(userId, userId));
    }

    [Fact]
    public void EmailOwnedByAnotherUserConflicts() =>
        Assert.Equal(
            EmailOwnershipDecision.ConflictWithOtherUser,
            ExternalConnectionPolicy.DecideEmailOwnership(
                new UserId(Guid.NewGuid()),
                new UserId(Guid.NewGuid())));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveProductionConnectionCountsCannotDisconnect(int productionConnectionCount) =>
        Assert.False(ExternalConnectionPolicy.CanDisconnect(
            null,
            ExternalProvider.Google,
            productionConnectionCount));

    [Fact]
    public void CurrentOrLastProductionConnectionCannotBeDisconnected()
    {
        Assert.False(ExternalConnectionPolicy.CanDisconnect(
            ExternalProvider.Google, ExternalProvider.Google, 2));
        Assert.False(ExternalConnectionPolicy.CanDisconnect(
            null, ExternalProvider.Google, 1));
        Assert.True(ExternalConnectionPolicy.CanDisconnect(
            null, ExternalProvider.Google, 2));
    }
}
