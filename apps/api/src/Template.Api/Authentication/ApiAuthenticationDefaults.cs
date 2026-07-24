using Template.Infrastructure.Authentication;

namespace Template.Api.Authentication;

internal static class ApiAuthenticationDefaults
{
    internal const string SchemeName =
        BrowserSessionAuthenticationDefaults.PrimaryScheme;
    internal const string IssuerSchemeName =
        BrowserSessionAuthenticationDefaults.IssuerScheme;
    internal const string CookieName =
        BrowserSessionAuthenticationDefaults.CookieName;
}
