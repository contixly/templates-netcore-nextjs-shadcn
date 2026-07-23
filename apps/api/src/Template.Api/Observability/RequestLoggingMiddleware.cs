using System.Diagnostics;

namespace Template.Api.Observability;

internal sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly EventId CompletionEvent = new(1000, "ApiRequestCompleted");

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status200OK;

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
                context.Request.Path.Value ?? "/",
                statusCode,
                Math.Round(elapsed, 3));
        }
    }

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
