using System.Security.Claims;
using Template.Application.Accounts;
using Template.Domain.Accounts;
using Template.Infrastructure.Authentication;

namespace Template.Api.Tests.Accounts;

public sealed class ExternalIdentityNormalizerTests
{
    private static readonly CancellationToken Ct =
        TestContext.Current.CancellationToken;

    [Fact]
    public async Task GoogleAcceptsOnlyVerifiedOidcEmail()
    {
        var tokens = Tokens();
        var result = await Normalizer().NormalizeAsync(
            ExternalProvider.Google,
            Principal(
                ("sub", "google-subject"),
                ("email", " owner@example.test "),
                ("email_verified", "true"),
                ("name", "  Example Owner  "),
                ("picture", "https://images.example.test/owner.png")),
            tokens,
            Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("google-subject", result.Identity!.Subject);
        Assert.Equal("owner@example.test", result.Identity.Email.Value);
        Assert.Equal("Example Owner", result.Identity.DisplayName);
        Assert.Equal(
            "https://images.example.test/owner.png",
            result.Identity.ImageUrl!.AbsoluteUri);
        Assert.Empty(tokens);
    }

    [Fact]
    public async Task GoogleRejectsUnverifiedEmail()
    {
        var result = await Normalizer().NormalizeAsync(
            ExternalProvider.Google,
            Principal(
                ("sub", "123"),
                ("email", "owner@example.test"),
                ("email_verified", "false")),
            Tokens(),
            Ct);

        Assert.Equal(AccountFailure.EmailUnverified, result.Failure);
        Assert.Null(result.Identity);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    public async Task GitLabRequiresExplicitVerifiedEmail(string verified)
    {
        var result = await Normalizer().NormalizeAsync(
            ExternalProvider.GitLab,
            Principal(
                ("sub", "gitlab-subject"),
                ("email", "owner@example.test"),
                ("email_verified", verified)),
            Tokens(),
            Ct);

        Assert.Equal(AccountFailure.EmailUnverified, result.Failure);
    }

    [Fact]
    public async Task GitHubUsesThePrimaryVerifiedEmailFromUserInfo()
    {
        var userInfo = new StubExternalUserInfoClient(
        [
            new ExternalProviderEmail("secondary@example.test", false, true),
            new ExternalProviderEmail("primary@example.test", true, true),
            new ExternalProviderEmail("unverified@example.test", false, false)
        ]);
        var tokens = Tokens();

        var result = await Normalizer(userInfo).NormalizeAsync(
            ExternalProvider.GitHub,
            Principal(
                ("id", "000123"),
                ("name", "GitHub Owner"),
                ("avatar_url", "https://avatars.example.test/123")),
            tokens,
            Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("123", result.Identity!.Subject);
        Assert.Equal("primary@example.test", result.Identity.Email.Value);
        Assert.Equal("ephemeral-access-token", userInfo.ObservedAccessToken);
        Assert.Empty(tokens);
    }

    [Fact]
    public async Task GitHubRejectsAnUnverifiedPrimaryEmail()
    {
        var userInfo = new StubExternalUserInfoClient(
        [
            new ExternalProviderEmail("primary@example.test", true, false),
            new ExternalProviderEmail("secondary@example.test", false, true)
        ]);

        var result = await Normalizer(userInfo).NormalizeAsync(
            ExternalProvider.GitHub,
            Principal(("id", "123")),
            Tokens(),
            Ct);

        Assert.Equal(AccountFailure.EmailUnverified, result.Failure);
    }

    [Fact]
    public async Task VkTreatsScopedUserInfoEmailAsProviderConfirmed()
    {
        var result = await Normalizer().NormalizeAsync(
            ExternalProvider.Vk,
            Principal(
                ("user_id", "vk-stable-subject"),
                ("email", "vk-owner@example.test"),
                ("first_name", "VK"),
                ("last_name", "Owner")),
            Tokens(),
            Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("vk-stable-subject", result.Identity!.Subject);
        Assert.Equal("vk-owner@example.test", result.Identity.Email.Value);
        Assert.Equal("VK Owner", result.Identity.DisplayName);
    }

    [Fact]
    public async Task YandexUsesDefaultEmailAndStableStringId()
    {
        var result = await Normalizer().NormalizeAsync(
            ExternalProvider.Yandex,
            Principal(
                ("id", "1000034426"),
                ("default_email", "yandex-owner@example.test"),
                ("display_name", "Yandex Owner"),
                ("default_avatar_id", "131652443")),
            Tokens(),
            Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("1000034426", result.Identity!.Subject);
        Assert.Equal("yandex-owner@example.test", result.Identity.Email.Value);
        Assert.Equal("Yandex Owner", result.Identity.DisplayName);
        Assert.Equal(
            "https://avatars.yandex.net/get-yapic/131652443/islands-200",
            result.Identity.ImageUrl!.AbsoluteUri);
    }

    [Theory]
    [InlineData("google", "sub")]
    [InlineData("gitlab", "sub")]
    [InlineData("vk", "user_id")]
    [InlineData("yandex", "id")]
    public async Task MissingProviderEmailIsRejected(
        string providerValue,
        string subjectClaim)
    {
        Assert.True(ExternalProvider.TryParse(providerValue, out var provider));

        var result = await Normalizer().NormalizeAsync(
            provider,
            Principal((subjectClaim, "stable-subject")),
            Tokens(),
            Ct);

        Assert.Equal(AccountFailure.EmailRequired, result.Failure);
    }

    [Fact]
    public async Task NonHttpsAvatarIsDiscardedWithoutRejectingIdentity()
    {
        var result = await Normalizer().NormalizeAsync(
            ExternalProvider.Google,
            Principal(
                ("sub", "123"),
                ("email", "owner@example.test"),
                ("email_verified", "true"),
                ("picture", "http://images.example.test/owner.png")),
            Tokens(),
            Ct);

        Assert.True(result.Succeeded);
        Assert.Null(result.Identity!.ImageUrl);
    }

    [Theory]
    [InlineData("not-numeric")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task GitHubRejectsAnUnstableSubject(string subject)
    {
        var result = await Normalizer(
                new StubExternalUserInfoClient(
                [
                    new ExternalProviderEmail(
                        "owner@example.test",
                        true,
                        true)
                ]))
            .NormalizeAsync(
                ExternalProvider.GitHub,
                Principal(("id", subject)),
                Tokens(),
                Ct);

        Assert.Equal(AccountFailure.IdentityConflict, result.Failure);
    }

    [Fact]
    public async Task EphemeralTokensAreClearedWhenUserInfoFails()
    {
        var tokens = Tokens();
        var normalizer = Normalizer(new ThrowingExternalUserInfoClient());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            normalizer.NormalizeAsync(
                ExternalProvider.GitHub,
                Principal(("id", "123")),
                tokens,
                Ct));

        Assert.Empty(tokens);
    }

    private static ExternalIdentityNormalizer Normalizer(
        IExternalUserInfoClient? userInfo = null) =>
        new(userInfo ?? new StubExternalUserInfoClient([]));

    private static Dictionary<string, string> Tokens() =>
        new(StringComparer.Ordinal)
        {
            [ExternalIdentityNormalizer.BackchannelAccessTokenName] =
                "ephemeral-access-token",
            ["refresh_token"] = "ephemeral-refresh-token"
        };

    private static ClaimsPrincipal Principal(
        params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)),
            "External"));

    private sealed class StubExternalUserInfoClient(
        IReadOnlyList<ExternalProviderEmail> emails)
        : IExternalUserInfoClient
    {
        public string? ObservedAccessToken { get; private set; }

        public Task<IReadOnlyList<ExternalProviderEmail>> GetGitHubEmailsAsync(
            string accessToken,
            CancellationToken cancellationToken)
        {
            ObservedAccessToken = accessToken;
            return Task.FromResult(emails);
        }
    }

    private sealed class ThrowingExternalUserInfoClient
        : IExternalUserInfoClient
    {
        public Task<IReadOnlyList<ExternalProviderEmail>> GetGitHubEmailsAsync(
            string accessToken,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Sanitized remote failure.");
    }
}
