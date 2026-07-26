using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Template.Api.Tests.Infrastructure;

internal sealed record CapturedLog(
    LogLevel Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, object?> State,
    IReadOnlyDictionary<string, object?> Scope,
    Exception? Exception);

internal sealed class CapturedLogProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> logs = new();
    private IExternalScopeProvider scopeProvider = new LoggerExternalScopeProvider();

    internal IReadOnlyCollection<CapturedLog> Logs => logs.ToArray();

    internal void Clear()
    {
        while (logs.TryDequeue(out _))
        {
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        new CapturedLogger(categoryName, logs, () => scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider provider) =>
        scopeProvider = provider;

    public void Dispose()
    {
    }

    private sealed class CapturedLogger(
        string category,
        ConcurrentQueue<CapturedLog> target,
        Func<IExternalScopeProvider> getScopeProvider)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            getScopeProvider().Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var stateValues = ToDictionary(state);
            var scopeValues = new Dictionary<string, object?>(StringComparer.Ordinal);
            getScopeProvider().ForEachScope(
                (scope, values) =>
                {
                    foreach (var pair in ToDictionary(scope))
                    {
                        values[pair.Key] = pair.Value;
                    }
                },
                scopeValues);
            target.Enqueue(new CapturedLog(
                logLevel,
                category,
                formatter(state, exception),
                stateValues,
                scopeValues,
                exception));
        }

        private static IReadOnlyDictionary<string, object?> ToDictionary<TState>(TState state)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                return values.ToDictionary(pair => pair.Key, pair => pair.Value);
            }

            return new Dictionary<string, object?>();
        }
    }
}
