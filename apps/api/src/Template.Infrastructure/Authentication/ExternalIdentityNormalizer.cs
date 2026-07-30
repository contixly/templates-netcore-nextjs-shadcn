using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Template.Application.Accounts;
using Template.Domain.Accounts;

namespace Template.Infrastructure.Authentication;

public sealed record ExternalIdentityResult(
    ExternalIdentity? Identity,
    AccountFailure? Failure)
{
    public bool Succeeded => Failure is null;
}

public sealed record ExternalProviderEmail(
    string Email,
    bool Primary,
    bool Verified);

public interface IExternalUserInfoClient
{
    Task<IReadOnlyList<ExternalProviderEmail>> GetGitHubEmailsAsync(
        string accessToken,
        CancellationToken cancellationToken);
}

public interface IExternalIdentityNormalizer
{
    Task<ExternalIdentityResult> NormalizeAsync(
        ExternalProvider provider,
        ClaimsPrincipal principal,
        IReadOnlyDictionary<string, string> ephemeralTokens,
        CancellationToken cancellationToken);
}

public sealed class ExternalIdentityNormalizer(
    IExternalUserInfoClient userInfo)
    : IExternalIdentityNormalizer
{
    public const string BackchannelAccessTokenName =
        "backchannel_access_token";

    private const int MaximumSubjectLength = 512;
    private const int MaximumDisplayNameLength = 50;
    private const int MaximumImageUrlLength = 2048;

    public async Task<ExternalIdentityResult> NormalizeAsync(
        ExternalProvider provider,
        ClaimsPrincipal principal,
        IReadOnlyDictionary<string, string> ephemeralTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(ephemeralTokens);

        try
        {
            var subject = NormalizeSubject(provider, principal);
            if (subject is null)
            {
                return Failed(AccountFailure.IdentityConflict);
            }

            var email = provider.Value switch
            {
                "google" or "gitlab" => NormalizeVerifiedOidcEmail(principal),
                "github" => await NormalizeGitHubEmailAsync(
                    ephemeralTokens,
                    cancellationToken),
                "vk" => NormalizeProviderConfirmedEmail(
                    ClaimValue(principal, "email", ClaimTypes.Email)),
                "yandex" => NormalizeProviderConfirmedEmail(
                    ClaimValue(
                        principal,
                        "default_email",
                        "email",
                        ClaimTypes.Email)),
                _ => FailedEmail(AccountFailure.IdentityConflict)
            };

            if (email.Failure is not null)
            {
                return Failed(email.Failure.Value);
            }

            return new ExternalIdentityResult(
                new ExternalIdentity(
                    provider,
                    subject,
                    email.Email!,
                    NormalizeDisplayName(provider, principal),
                    NormalizeImageUrl(provider, principal)),
                null);
        }
        finally
        {
            ClearEphemeralTokens(ephemeralTokens);
        }
    }

    private async Task<NormalizedEmail> NormalizeGitHubEmailAsync(
        IReadOnlyDictionary<string, string> ephemeralTokens,
        CancellationToken cancellationToken)
    {
        var accessToken = AccessToken(ephemeralTokens);
        if (string.IsNullOrEmpty(accessToken))
        {
            return FailedEmail(AccountFailure.EmailRequired);
        }

        var emails = await userInfo.GetGitHubEmailsAsync(
            accessToken,
            cancellationToken);
        var primary = emails.Where(email => email.Primary).ToArray();
        if (primary.Length is 0)
        {
            return FailedEmail(AccountFailure.EmailRequired);
        }

        if (primary.Length is not 1 || !primary[0].Verified)
        {
            return FailedEmail(AccountFailure.EmailUnverified);
        }

        return NormalizeProviderConfirmedEmail(primary[0].Email);
    }

    private static NormalizedEmail NormalizeVerifiedOidcEmail(
        ClaimsPrincipal principal)
    {
        var email = ClaimValue(principal, "email", ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return FailedEmail(AccountFailure.EmailRequired);
        }

        var verified = principal.FindAll("email_verified")
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (verified.Length is not 1
            || !bool.TryParse(verified[0], out var isVerified)
            || !isVerified)
        {
            return FailedEmail(AccountFailure.EmailUnverified);
        }

        return NormalizeProviderConfirmedEmail(email);
    }

    private static NormalizedEmail NormalizeProviderConfirmedEmail(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FailedEmail(AccountFailure.EmailRequired);
        }

        try
        {
            return new NormalizedEmail(VerifiedEmail.Create(value), null);
        }
        catch (ArgumentException)
        {
            return FailedEmail(AccountFailure.EmailRequired);
        }
    }

    private static string? NormalizeSubject(
        ExternalProvider provider,
        ClaimsPrincipal principal)
    {
        var value = provider.Value switch
        {
            "google" or "gitlab" => ClaimValue(
                principal,
                "sub",
                ClaimTypes.NameIdentifier),
            "github" => ClaimValue(
                principal,
                "id",
                ClaimTypes.NameIdentifier),
            "vk" => ClaimValue(
                principal,
                "user_id",
                "sub",
                ClaimTypes.NameIdentifier),
            "yandex" => ClaimValue(
                principal,
                "id",
                "sub",
                ClaimTypes.NameIdentifier),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > MaximumSubjectLength
            || value.Any(char.IsControl))
        {
            return null;
        }

        if (provider == ExternalProvider.GitHub)
        {
            return ulong.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var identifier)
                && identifier > 0
                    ? identifier.ToString(CultureInfo.InvariantCulture)
                    : null;
        }

        return value;
    }

    private static string? NormalizeDisplayName(
        ExternalProvider provider,
        ClaimsPrincipal principal)
    {
        var value = ClaimValue(
            principal,
            "name",
            ClaimTypes.Name,
            "display_name",
            "login");
        if (string.IsNullOrWhiteSpace(value)
            && provider == ExternalProvider.Vk)
        {
            var firstName = ClaimValue(principal, "first_name");
            var lastName = ClaimValue(principal, "last_name");
            value = string.Join(
                ' ',
                new[] { firstName, lastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length is >= 2 and <= MaximumDisplayNameLength
            && !normalized.Any(char.IsControl)
                ? normalized
                : null;
    }

    private static Uri? NormalizeImageUrl(
        ExternalProvider provider,
        ClaimsPrincipal principal)
    {
        var value = ClaimValue(
            principal,
            "picture",
            "avatar_url",
            "avatar",
            "photo_200");
        if (string.IsNullOrWhiteSpace(value)
            && provider == ExternalProvider.Yandex)
        {
            value = CreateYandexAvatarUrl(
                ClaimValue(principal, "default_avatar_id"));
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsoluteUri.Length <= MaximumImageUrlLength
                ? uri
                : null;
    }

    private static string? CreateYandexAvatarUrl(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)
            || identifier.Length > 256
            || identifier.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-' and not '/')
            || identifier.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        return $"https://avatars.yandex.net/get-yapic/{identifier}/islands-200";
    }

    private static string? ClaimValue(
        ClaimsPrincipal principal,
        params string[] types)
    {
        foreach (var type in types)
        {
            var values = principal.FindAll(type)
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (values.Length is 1)
            {
                return values[0];
            }

            if (values.Length > 1)
            {
                return null;
            }
        }

        return null;
    }

    private static string? AccessToken(
        IReadOnlyDictionary<string, string> ephemeralTokens)
    {
        if (ephemeralTokens.TryGetValue(
                BackchannelAccessTokenName,
                out var token)
            && !string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        return ephemeralTokens.TryGetValue("access_token", out token)
            && !string.IsNullOrWhiteSpace(token)
                ? token
                : null;
    }

    private static void ClearEphemeralTokens(
        IReadOnlyDictionary<string, string> tokens)
    {
        if (tokens is IDictionary<string, string> dictionary
            && !dictionary.IsReadOnly)
        {
            dictionary.Clear();
        }
    }

    private static ExternalIdentityResult Failed(AccountFailure failure) =>
        new(null, failure);

    private static NormalizedEmail FailedEmail(AccountFailure failure) =>
        new(null, failure);

    private sealed record NormalizedEmail(
        VerifiedEmail? Email,
        AccountFailure? Failure);
}

internal sealed class GitHubExternalUserInfoClient(HttpClient client)
    : IExternalUserInfoClient
{
    private const int MaximumResponseSize = 64 * 1024;

    public async Task<IReadOnlyList<ExternalProviderEmail>>
        GetGitHubEmailsAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "user/emails?per_page=100");
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(
            MaximumResponseSize,
            cancellationToken);
        var emails = await response.Content
            .ReadFromJsonAsync<GitHubEmailResponse[]>(
                cancellationToken: cancellationToken)
            ?? [];
        return emails
            .Take(100)
            .Select(email => new ExternalProviderEmail(
                email.Email ?? string.Empty,
                email.Primary,
                email.Verified))
            .ToArray();
    }

    private sealed class GitHubEmailResponse
    {
        [JsonPropertyName("email")]
        public string? Email { get; init; }

        [JsonPropertyName("primary")]
        public bool Primary { get; init; }

        [JsonPropertyName("verified")]
        public bool Verified { get; init; }
    }
}
