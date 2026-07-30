using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Primitives;
using OpenIddict.Client;
using Template.Application.Accounts;
using Template.Application.Authentication;
using Template.Domain.Accounts;

namespace Template.Api.Authentication;

internal sealed class ExternalOAuthChallengeService(
    OpenIddictClientService registrations)
{
    internal async Task<string> CreateAuthorizationUrlAsync(
        HttpContext context,
        ExternalProvider provider,
        ExternalAuthIntent intent,
        string returnPath,
        AuthenticatedSession? current,
        string authenticationScheme,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);

        var configuration =
            await registrations.GetServerConfigurationByRegistrationIdAsync(
                provider.Value,
                cancellationToken);
        var expectedEndpoint = configuration.AuthorizationEndpoint
            ?? throw new InvalidOperationException(
                "The external provider has no authorization endpoint.");

        var properties = new AuthenticationProperties
        {
            RedirectUri = returnPath
        };
        properties.Items[ExternalOAuthStateProperties.Provider] =
            provider.Value;
        properties.Items[ExternalOAuthStateProperties.Intent] =
            ExternalOAuthStateProperties.FormatIntent(intent);
        properties.Items[ExternalOAuthStateProperties.ReturnPath] =
            returnPath;
        if (intent == ExternalAuthIntent.Connect)
        {
            var initiating = current ??
                throw new InvalidOperationException(
                    "A current session is required for a connect challenge.");
            properties.Items[ExternalOAuthStateProperties.UserId] =
                initiating.User.Id.Value.ToString("D");
            properties.Items[ExternalOAuthStateProperties.SessionId] =
                initiating.Session.Id.Value.ToString("D");
        }

        await context.ChallengeAsync(authenticationScheme, properties);

        if (context.Response.HasStarted
            || !context.Response.Headers.TryGetValue(
                "Location",
                out StringValues locations)
            || locations.Count != 1
            || !Uri.TryCreate(
                locations[0],
                UriKind.Absolute,
                out var authorizationUrl)
            || authorizationUrl.Scheme != Uri.UriSchemeHttps
            || !IsExpectedAuthorizationEndpoint(
                authorizationUrl,
                expectedEndpoint))
        {
            throw new InvalidOperationException(
                "The external provider challenge did not produce one expected HTTPS authorization URL.");
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.Headers.Remove("Location");
        context.Response.ContentLength = null;
        return authorizationUrl.AbsoluteUri;
    }

    private static bool IsExpectedAuthorizationEndpoint(
        Uri actual,
        Uri expected) =>
        expected.IsAbsoluteUri
        && expected.Scheme == Uri.UriSchemeHttps
        && string.Equals(actual.Scheme, expected.Scheme, StringComparison.Ordinal)
        && string.Equals(actual.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
        && actual.Port == expected.Port
        && string.Equals(
            actual.AbsolutePath,
            expected.AbsolutePath,
            StringComparison.Ordinal);
}

internal static class ExternalOAuthStateProperties
{
    private const string Prefix = "Template.ExternalOAuth.";

    internal const string Provider = Prefix + "Provider";
    internal const string Intent = Prefix + "Intent";
    internal const string ReturnPath = Prefix + "ReturnPath";
    internal const string UserId = Prefix + "UserId";
    internal const string SessionId = Prefix + "SessionId";

    internal static string FormatIntent(ExternalAuthIntent intent) =>
        intent switch
        {
            ExternalAuthIntent.SignIn => "signIn",
            ExternalAuthIntent.Connect => "connect",
            _ => throw new ArgumentOutOfRangeException(nameof(intent))
        };

    internal static bool TryParseIntent(
        string? value,
        out ExternalAuthIntent intent)
    {
        switch (value)
        {
            case "signIn":
                intent = ExternalAuthIntent.SignIn;
                return true;
            case "connect":
                intent = ExternalAuthIntent.Connect;
                return true;
            default:
                intent = default;
                return false;
        }
    }
}
