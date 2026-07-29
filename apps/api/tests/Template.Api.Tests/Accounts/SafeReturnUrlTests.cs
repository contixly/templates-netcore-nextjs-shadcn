using Template.Api.Authentication;

namespace Template.Api.Tests.Accounts;

public sealed class SafeReturnUrlTests
{
    [Theory]
    [InlineData("/safe/..//evil.example")]
    [InlineData("/%2e%2e//evil.example")]
    public void CanonicalNetworkPathsAreRejected(string candidate)
    {
        var accepted = SafeReturnUrl.TryNormalize(
            candidate,
            "/dashboard",
            out var normalized);

        Assert.False(accepted);
        Assert.Empty(normalized);
    }
}
