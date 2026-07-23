using System.Diagnostics;

namespace Template.Api.Observability;

internal sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ILogger<CorrelationIdMiddleware> logger)
{
    internal const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "Template.Api.TraceId";

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = TryGetAcceptedHeader(context.Request, out var accepted)
            ? accepted
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        context.Items[ItemKey] = traceId;
        context.Response.Headers[HeaderName] = traceId;
        context.Response.OnStarting(
            static state =>
            {
                var (response, correlationId) = ((HttpResponse, string))state;
                response.Headers[HeaderName] = correlationId;
                return Task.CompletedTask;
            },
            (context.Response, traceId));

        using (logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = traceId }))
        {
            await next(context);
        }
    }

    internal static string GetTraceId(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) && value is string traceId
            ? traceId
            : context.TraceIdentifier;

    private static bool TryGetAcceptedHeader(HttpRequest request, out string value)
    {
        value = string.Empty;
        if (!request.Headers.TryGetValue(HeaderName, out var values) || values.Count != 1)
        {
            return false;
        }

        var candidate = values.ToString();
        if (candidate.Length is < 1 or > 64 ||
            candidate.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not ('.' or '_' or '-')))
        {
            return false;
        }

        value = candidate;
        return true;
    }
}
