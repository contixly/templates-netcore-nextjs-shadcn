using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Observability;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class ObservabilityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task AcceptedCorrelationIdMatchesHeaderProblemAndLogScope()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, "client.trace-123");

        using var response = await client.GetAsync(
            "/api/does-not-exist?secret=query-value",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "client.trace-123",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var problem = await response.Content.ReadFromJsonAsync<ProblemTrace>(
            TestContext.Current.CancellationToken);
        Assert.Equal("client.trace-123", problem!.TraceId);
        var completion = Assert.Single(
            logs.Logs,
            log => log.State.TryGetValue(
                "{OriginalFormat}",
                out var format) &&
                Equals(format, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms"));
        Assert.Equal("client.trace-123", completion.Scope["TraceId"]);
        Assert.Equal("/api/does-not-exist", completion.State["Path"]);
        Assert.DoesNotContain("query-value", completion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCorrelationIdIsIgnoredWithoutRejectingRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            CorrelationIdMiddleware.HeaderName,
            "invalid value with spaces");

        using var response = await client.GetAsync(
            "/api/v1/system/status",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.NotEqual("invalid value with spaces", actual);
        Assert.NotEmpty(actual);
    }

    [Fact]
    public async Task HealthCompletionIsLoggedAtDebug()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            logs.Logs,
            log => log.Category.EndsWith(nameof(RequestLoggingMiddleware), StringComparison.Ordinal) &&
                   log.Level == LogLevel.Debug);
    }

    private sealed record ProblemTrace(string TraceId);
}
