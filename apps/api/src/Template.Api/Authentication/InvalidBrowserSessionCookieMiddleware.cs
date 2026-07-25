using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Template.Infrastructure.Authentication;

namespace Template.Api.Authentication;

internal sealed class InvalidBrowserSessionCookieMiddleware(
    RequestDelegate next)
{
    public Task InvokeAsync(
        HttpContext context,
        IOptionsMonitor<CookieAuthenticationOptions> options)
    {
        if (BrowserSessionCookieInvalidation.IsRequested(context))
        {
            var cookieOptions = options.Get(ApiAuthenticationDefaults.SchemeName);
            cookieOptions.CookieManager.DeleteCookie(
                context,
                cookieOptions.Cookie.Name!,
                cookieOptions.Cookie.Build(context));
        }

        return next(context);
    }
}
