using Template.Api.Authentication;

namespace Template.Api.Tests.Accounts;

public sealed class SafeReturnUrlTests
{
    [Theory]
    [InlineData("/search?next=%2Fdashboard")]
    [InlineData("/search?value=%25complete")]
    [InlineData("/search#next=%2Fdashboard")]
    [InlineData("/search#value=%25complete")]
    public void EncodedQueryAndFragmentValuesArePreserved(string candidate)
    {
        var accepted = SafeReturnUrl.TryNormalize(
            candidate,
            "/dashboard",
            out var normalized);

        Assert.True(accepted);
        Assert.Equal(candidate, normalized);
    }

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

    [Theory]
    [InlineData("/%252f%252fevil.example")]
    [InlineData("/safe/%255c%255cevil.example")]
    [InlineData("/safe%250apath")]
    public void RepeatedlyEncodedPathSeparatorsAndControlsAreRejected(
        string candidate)
    {
        var accepted = SafeReturnUrl.TryNormalize(
            candidate,
            "/dashboard",
            out var normalized);

        Assert.False(accepted);
        Assert.Empty(normalized);
    }
}
