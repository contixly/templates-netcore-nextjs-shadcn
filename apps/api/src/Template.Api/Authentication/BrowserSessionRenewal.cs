using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Template.Api.Authentication;

internal static class BrowserSessionRenewal
{
    internal const string SuppressionHeaderName =
        "X-Template-Session-Renewal";
    internal const string SuppressionHeaderValue = "suppress";
    private const string SessionPath = "/api/v1/auth/session";
    private static readonly object SynchronousRenewalKey = new();

    internal static bool IsSuppressed(HttpContext context) =>
        IsSessionRead(context) &&
        string.Equals(
            context.Request.Headers[SuppressionHeaderName].ToString(),
            SuppressionHeaderValue,
            StringComparison.Ordinal);

    internal static void HandleSlidingExpiration(
        CookieSlidingExpirationContext context)
    {
        if (!IsSessionRead(context.HttpContext))
        {
            return;
        }

        if (IsSuppressed(context.HttpContext))
        {
            context.ShouldRenew = false;
            return;
        }

        if (context.ShouldRenew)
        {
            context.HttpContext.Items[SynchronousRenewalKey] = true;
            context.ShouldRenew = false;
        }
    }

    internal static async Task RenewIfRequestedAsync(HttpContext context)
    {
        if (!context.Items.Remove(SynchronousRenewalKey))
        {
            return;
        }

        var result = await context.AuthenticateAsync(
            ApiAuthenticationDefaults.SchemeName);
        if (!result.Succeeded ||
            result.Principal is null ||
            result.Properties?.IssuedUtc is not { } issuedAt ||
            result.Properties.ExpiresUtc is not { } expiresAt)
        {
            return;
        }

        var properties = new AuthenticationProperties();
        foreach (var item in result.Properties.Items)
        {
            properties.Items[item.Key] = item.Value;
        }

        var renewedAt = context.RequestServices
            .GetRequiredService<TimeProvider>()
            .GetUtcNow();
        properties.IssuedUtc = renewedAt;
        properties.ExpiresUtc = renewedAt + (expiresAt - issuedAt);
        await context.SignInAsync(
            ApiAuthenticationDefaults.SchemeName,
            result.Principal,
            properties);
    }

    private static bool IsSessionRead(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) &&
        string.Equals(
            context.Request.Path.Value,
            SessionPath,
            StringComparison.OrdinalIgnoreCase);
}
