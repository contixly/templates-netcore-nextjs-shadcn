using Template.Application.Authentication;

namespace Template.Application.Tests;

public sealed class LocalAutomationCredentialPolicyTests
{
    [Theory]
    [InlineData(" LOCAL-AGENT+Case@LOCAL-AGENT.TEST ", "local-agent+case@local-agent.test")]
    [InlineData("local-agent+abc@local-agent.test", "local-agent+abc@local-agent.test")]
    public void NormalizeEmailTrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, LocalAutomationCredentialPolicy.NormalizeEmail(input));
    }

    [Theory]
    [InlineData("local-agent+abc@local-agent.test", true)]
    [InlineData("local-agent+@local-agent.test", false)]
    [InlineData("local-agent+a@b@local-agent.test", false)]
    [InlineData("person@example.com", false)]
    [InlineData("local-agent+abc@example.com", false)]
    public void IsLocalEmailRequiresTheReservedNamespace(string input, bool expected)
    {
        Assert.Equal(expected, LocalAutomationCredentialPolicy.IsLocalEmail(input));
    }

    [Fact]
    public void NormalizeNameTrimsVisibleName()
    {
        Assert.Equal(
            "Local Automation User",
            LocalAutomationCredentialPolicy.NormalizeName("  Local Automation User  "));
    }
}
