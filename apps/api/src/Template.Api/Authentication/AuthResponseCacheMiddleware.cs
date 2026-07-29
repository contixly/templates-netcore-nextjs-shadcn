namespace Template.Api.Authentication;

internal sealed class AuthResponseCacheMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/local-auth") ||
            context.Request.Path.StartsWithSegments("/api/v1/auth") ||
            context.Request.Path.StartsWithSegments("/api/auth"))
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }

        return next(context);
    }
}
