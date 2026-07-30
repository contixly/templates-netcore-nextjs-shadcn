using System.Diagnostics;

namespace Template.Api.Observability;

internal sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly EventId CompletionEvent = new(1000, "ApiRequestCompleted");
    private static readonly object SafePathItemKey = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status200OK;
        var safePath = ResolveSafePath(context);
        context.Items[SafePathItemKey] = safePath;

        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        catch (BadHttpRequestException exception)
        {
            statusCode = exception.StatusCode;
            throw;
        }
        catch
        {
            statusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            var level = ResolveLevel(context.Request.Path, statusCode);
            logger.Log(
                level,
                CompletionEvent,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                safePath,
                statusCode,
                Math.Round(elapsed, 3));
        }
    }

    internal static string SafePath(HttpContext context) =>
        context.Items.TryGetValue(SafePathItemKey, out var value) &&
        value is string safePath
            ? safePath
            : ResolveSafePath(context);

    private static string ResolveSafePath(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? "/api/{route}"
            : "/api/{unmatched}";

    private static LogLevel ResolveLevel(PathString path, int statusCode)
    {
        if (path.StartsWithSegments("/api/health"))
        {
            return LogLevel.Debug;
        }

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogLevel.Error;
        }

        return statusCode >= StatusCodes.Status400BadRequest
            ? LogLevel.Warning
            : LogLevel.Information;
    }
}
