namespace Template.Infrastructure.Authentication;

public static class BrowserSessionAuthenticationDefaults
{
    public const string PrimaryScheme = "Template.Session";
    public const string IssuerScheme = "Template.Session.Issuer";
    public const string CookieName = "__Host-template.session";
    public const string TicketDataProtectionPurpose =
        "Template.Api.Authentication.BrowserSessionCookie.v1";
}
