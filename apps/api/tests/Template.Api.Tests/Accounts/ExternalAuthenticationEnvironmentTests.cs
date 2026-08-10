extern alias E2EHost;

namespace Template.Api.Tests.Accounts;

public sealed class ExternalAuthenticationEnvironmentTests
{
    [Fact]
    public void VkClientIdOnlyIsForwardedWithoutItsLegacySecret()
    {
        var source = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ExternalAuthentication__Providers__Vk__ClientId"] = "vk-id",
            ["ExternalAuthentication__Providers__Google__ClientId"] =
                "google-id",
            ["ExternalAuthentication__Providers__GitHub__ClientId"] =
                "github-id",
            ["ExternalAuthentication__Providers__GitHub__ClientSecret"] =
                "github-secret"
        };
        var target = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ExternalAuthentication__Providers__Vk__ClientSecret"] =
                "stale-secret"
        };

        E2EHost::Template.E2EHost.ExternalAuthenticationEnvironment.CopyConfiguredValues(
            target,
            name => source.GetValueOrDefault(name));

        Assert.Equal(
            "vk-id",
            target["ExternalAuthentication__Providers__Vk__ClientId"]);
        Assert.DoesNotContain(
            "ExternalAuthentication__Providers__Vk__ClientSecret",
            target.Keys);
        Assert.DoesNotContain(
            "ExternalAuthentication__Providers__Google__ClientId",
            target.Keys);
        Assert.Equal(
            "github-id",
            target["ExternalAuthentication__Providers__GitHub__ClientId"]);
        Assert.Equal(
            "github-secret",
            target["ExternalAuthentication__Providers__GitHub__ClientSecret"]);
    }

    [Fact]
    public void VkLegacyClientSecretIsNotForwarded()
    {
        var source = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ExternalAuthentication__Providers__Vk__ClientId"] = "vk-id",
            ["ExternalAuthentication__Providers__Vk__ClientSecret"] =
                "legacy-vk-secret"
        };
        var target = new Dictionary<string, string?>(StringComparer.Ordinal);

        E2EHost::Template.E2EHost.ExternalAuthenticationEnvironment.CopyConfiguredValues(
            target,
            name => source.GetValueOrDefault(name));

        Assert.Equal(
            "vk-id",
            target["ExternalAuthentication__Providers__Vk__ClientId"]);
        Assert.DoesNotContain(
            "ExternalAuthentication__Providers__Vk__ClientSecret",
            target.Keys);
    }
}
