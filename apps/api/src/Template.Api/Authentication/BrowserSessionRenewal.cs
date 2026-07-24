namespace Template.Api.Authentication;

internal static class BrowserSessionRenewal
{
    internal const string SuppressionHeaderName =
        "X-Template-Session-Renewal";
    internal const string SuppressionHeaderValue = "suppress";
    private const string SessionPath = "/api/v1/auth/session";

    internal static bool IsSuppressed(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) &&
        string.Equals(
            context.Request.Path.Value,
            SessionPath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            context.Request.Headers[SuppressionHeaderName].ToString(),
            SuppressionHeaderValue,
            StringComparison.Ordinal);
}
