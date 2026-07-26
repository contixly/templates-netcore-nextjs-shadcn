using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace Template.Infrastructure.Authentication;

public sealed class PrimaryBrowserSessionCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager _inner = new();

    public string? GetRequestCookie(HttpContext context, string key) =>
        _inner.GetRequestCookie(context, key);

    public void AppendResponseCookie(
        HttpContext context,
        string key,
        string? value,
        CookieOptions options) =>
        _inner.AppendResponseCookie(context, key, value, options);

    public void DeleteCookie(
        HttpContext context,
        string key,
        CookieOptions options)
    {
        if (!BrowserSessionReplacement.TrySuppressPrimaryCookieDeletion(context))
        {
            _inner.DeleteCookie(context, key, options);
        }
    }
}

public sealed class WriteOnlyBrowserSessionCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager _inner = new();

    public string? GetRequestCookie(HttpContext context, string key) => null;

    public void AppendResponseCookie(
        HttpContext context,
        string key,
        string? value,
        CookieOptions options) =>
        _inner.AppendResponseCookie(context, key, value, options);

    public void DeleteCookie(
        HttpContext context,
        string key,
        CookieOptions options) =>
        _inner.DeleteCookie(context, key, options);
}

internal static class BrowserSessionReplacement
{
    private static readonly object StateKey = new();

    internal static void Begin(HttpContext context)
    {
        if (context.Items.ContainsKey(StateKey))
        {
            throw new InvalidOperationException(
                "Browser-session replacement already started for this request.");
        }

        context.Items[StateKey] = new ReplacementState();
    }

    internal static bool TrySuppressPrimaryCookieDeletion(HttpContext context)
    {
        if (!context.Items.TryGetValue(StateKey, out var value) ||
            value is not ReplacementState state ||
            !state.SuppressPrimaryCookieDeletion)
        {
            return false;
        }

        state.SuppressPrimaryCookieDeletion = false;
        return true;
    }

    private sealed class ReplacementState
    {
        internal bool SuppressPrimaryCookieDeletion { get; set; } = true;
    }
}
