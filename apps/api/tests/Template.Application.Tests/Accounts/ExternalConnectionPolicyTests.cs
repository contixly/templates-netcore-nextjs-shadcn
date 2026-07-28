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

    [Fact]
    public void DifferentFreeEmailCanBeAttachedAsSecondary() =>
        Assert.Equal(
            EmailOwnershipDecision.AttachSecondary,
            ExternalConnectionPolicy.DecideEmailOwnership(new UserId(Guid.NewGuid()), null));

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
