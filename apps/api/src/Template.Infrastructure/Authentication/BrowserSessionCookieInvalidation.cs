using Microsoft.AspNetCore.Http;

namespace Template.Infrastructure.Authentication;

public static class BrowserSessionCookieInvalidation
{
    private static readonly object StateKey = new();

    public static void Request(HttpContext context) =>
        context.Items[StateKey] = true;

    public static bool IsRequested(HttpContext context) =>
        context.Items.ContainsKey(StateKey);
}
