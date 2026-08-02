using Template.Domain.ApiKeys;
using Template.Domain.Organizations;

namespace Template.Application.Tests.ApiKeys;

public sealed partial class ApiKeyPolicyTests
{
    [Fact]
    public void OrganizationReadAllExpandsToFourReadScopes() =>
        Assert.Equal(
            ["organization:read", "member:read", "team:read", "teamMember:read"],
            ApiKeyPolicy.ExpandPresets(["organization-read-all"]));

    [Fact]
    public void Presets_expand_to_canonical_sorted_deduplicated_scopes() =>
        Assert.Equal(
            ["basic:read", "organization:read", "team:read", "teamMember:read"],
            ApiKeyPolicy.ExpandPresets(["organization-team-members-read", "basic-read", "organization-teams-read"]));

    [Theory]
    [InlineData(" Name ", true, "Name")]
    [InlineData("", false, null)]
    [InlineData("   ", false, null)]
    [InlineData("\u0001", false, null)]
    public void Names_are_trimmed_and_must_be_one_to_32_non_control_unicode_scalars(string value, bool valid, string? expected)
    {
        var result = ApiKeyPolicy.TryNormalizeName(value, out var actual);

        Assert.Equal(valid, result);
        if (result)
        {
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData("never", 0)]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    [InlineData("365d", 365)]
    public void Expiration_options_are_closed(string value, int days)
    {
        Assert.True(ApiKeyPolicy.TryGetExpiration(value, out var expiration));
        Assert.Equal(TimeSpan.FromDays(days), expiration ?? TimeSpan.Zero);
    }

    [Theory]
    [InlineData("1m", 60)]
    [InlineData("1h", 3600)]
    [InlineData("1d", 86400)]
    public void Rate_limit_windows_are_closed(string value, int seconds)
    {
        Assert.True(ApiKeyPolicy.TryGetRateLimitWindow(value, out var window));
        Assert.Equal(TimeSpan.FromSeconds(seconds), window);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, false)]
    public void Rate_limit_maximum_is_bounded(int value, bool valid) =>
        Assert.Equal(valid, ApiKeyPolicy.IsValidRateLimitMax(value));

    [Fact]
    public void Only_admins_and_owners_can_manage_api_keys()
    {
        Assert.False(OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Member).CanManageApiKeys);
        Assert.True(OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Admin).CanManageApiKeys);
        Assert.True(OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Owner).CanManageApiKeys);
    }
}

public sealed partial class ApiKeyPolicyTests
{
    [Fact]
    public void Presets_are_closed_and_reject_unknown_and_null_entries()
    {
        Assert.False(ApiKeyPolicy.AreValidPresets(["BASIC-read"]));
        Assert.False(ApiKeyPolicy.AreValidPresets([null!]));
        Assert.False(ApiKeyPolicy.AreValidPresets(["basic-read", null!]));
        Assert.Empty(ApiKeyPolicy.ExpandPresets(["basic-read", null!]));
    }

    [Theory]
    [InlineData("basic-read", new[] { "basic:read" })]
    [InlineData("organization-read", new[] { "organization:read" })]
    [InlineData("organization-members-read", new[] { "organization:read", "member:read" })]
    [InlineData("organization-teams-read", new[] { "organization:read", "team:read" })]
    [InlineData("organization-team-members-read", new[] { "organization:read", "team:read", "teamMember:read" })]
    [InlineData("organization-read-all", new[] { "organization:read", "member:read", "team:read", "teamMember:read" })]
    public void Every_preset_expands_to_its_exact_scopes(string preset, string[] expected) =>
        Assert.Equal(expected, ApiKeyPolicy.ExpandPresets([preset]));

    [Fact]
    public void Name_accepts_32_unicode_scalars_and_rejects_33_including_supplementary_scalars()
    {
        Assert.True(ApiKeyPolicy.TryNormalizeName(new string('a', 32), out _));
        Assert.False(ApiKeyPolicy.TryNormalizeName(new string('a', 33), out _));
        Assert.True(ApiKeyPolicy.TryNormalizeName(string.Concat(Enumerable.Repeat("😀", 32)), out _));
        Assert.False(ApiKeyPolicy.TryNormalizeName(string.Concat(Enumerable.Repeat("😀", 33)), out _));
    }

    [Theory]
    [InlineData("Never")]
    [InlineData("1d")]
    [InlineData("")]
    public void Expiration_rejects_unknown_or_case_variant_options(string value) =>
        Assert.False(ApiKeyPolicy.TryGetExpiration(value, out _));

    [Theory]
    [InlineData("1H")]
    [InlineData("7d")]
    [InlineData("")]
    public void Rate_windows_reject_unknown_or_case_variant_options(string value) =>
        Assert.False(ApiKeyPolicy.TryGetRateLimitWindow(value, out _));
}
