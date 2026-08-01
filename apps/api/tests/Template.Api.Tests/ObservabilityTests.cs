using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Observability;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class ObservabilityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    public static TheoryData<string, HttpStatusCode> HandledExceptionCases =>
        new()
        {
            { "/api/testing/bad-request", HttpStatusCode.BadRequest },
            { "/api/testing/fault", HttpStatusCode.InternalServerError },
        };

    public static TheoryData<string> RuntimeLoggingConfigurations =>
        new()
        {
            { "appsettings.json" },
            { "appsettings.Development.json" }
        };

    [Theory]
    [MemberData(nameof(RuntimeLoggingConfigurations))]
    public void RuntimeLoggingSuppressesRawHostingRequestUrls(string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, fileName)));
        var logLevels = document.RootElement
            .GetProperty("Logging")
            .GetProperty("LogLevel");

        Assert.Equal(
            "None",
            logLevels
                .GetProperty(CapturedLogProvider.RawRequestHostingCategory)
                .GetString());
    }

    [Fact]
    public async Task CapturedLogsSuppressRawHostingScopesWithoutLosingSafeObservability()
    {
        const string sensitivePath = "round13-sensitive-hosting-scope";
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        var loggerFactory = factory.Services.GetRequiredService<ILoggerFactory>();
        Assert.False(
            loggerFactory.CreateLogger(CapturedLogProvider.RawRequestHostingCategory)
                .IsEnabled(LogLevel.Critical));
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add(
            CorrelationIdMiddleware.HeaderName,
            "hosting-scope.trace-123");

        using var unmatched = await client.GetAsync(
            $"/api/{sensitivePath}",
            TestContext.Current.CancellationToken);
        using var health = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);
        loggerFactory
            .CreateLogger($"{CapturedLogProvider.AspNetCoreCategory}.SafeWarningProbe")
            .LogWarning("Safe framework warning probe");

        Assert.Equal(HttpStatusCode.NotFound, unmatched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.DoesNotContain(
            logs.Logs,
            log => log.Scope.ContainsKey("RequestPath"));
        Assert.DoesNotContain(
            logs.Logs,
            log => CapturedRenderingContains(log, sensitivePath));
        var applicationDebug = Assert.Single(
            logs.Logs,
            log => log.Category.EndsWith(
                       nameof(RequestLoggingMiddleware),
                       StringComparison.Ordinal) &&
                   log.Level == LogLevel.Debug &&
                   Equals(log.State["Path"], "/api/health/live"));
        Assert.Equal(
            "hosting-scope.trace-123",
            applicationDebug.Scope["TraceId"]);
        Assert.Contains(
            logs.Logs,
            log => log.Category ==
                       $"{CapturedLogProvider.AspNetCoreCategory}.SafeWarningProbe" &&
                   log.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AcceptedCorrelationIdMatchesHeaderProblemAndLogScope()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();
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
        Assert.Equal("/api/{unmatched}", completion.State["Path"]);
        Assert.DoesNotContain("query-value", completion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCorrelationIdIsIgnoredWithoutRejectingRequest()
    {
        using var client = factory.CreateApiClient();
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

    [Theory]
    [MemberData(nameof(HandledExceptionCases))]
    public async Task HandledExceptionsPreserveCorrelationHeader(
        string uri,
        HttpStatusCode expectedStatus)
    {
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add(
            CorrelationIdMiddleware.HeaderName,
            "exception.trace-123");

        using var response = await client.GetAsync(
            uri,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "exception.trace-123",
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
        var problem = await response.Content.ReadFromJsonAsync<ProblemTrace>(
            TestContext.Current.CancellationToken);
        Assert.Equal("exception.trace-123", problem!.TraceId);
    }

    [Fact]
    public async Task HealthCompletionIsLoggedAtDebug()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            logs.Logs,
            log => log.Category.EndsWith(nameof(RequestLoggingMiddleware), StringComparison.Ordinal) &&
                   log.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task HandledBadRequestLogsFinalClientStatusAtWarning()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/testing/bad-request",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var completion = Assert.Single(
            logs.Logs,
            log => log.State.TryGetValue(
                "{OriginalFormat}",
                out var format) &&
                Equals(format, "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms"));
        Assert.Equal("/api/testing/bad-request", completion.State["Path"]);
        Assert.Equal(400, completion.State["StatusCode"]);
        Assert.Equal(LogLevel.Warning, completion.Level);
    }

    [Fact]
    public async Task AntiforgeryFailureLogsFinalClientStatusAtWarning()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add(
            CorrelationIdMiddleware.HeaderName,
            "antiforgery.trace-123");

        using var response = await client.PostAsync(
            "/api/local-auth/testing-rate",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var completion = FindCompletion(logs, "/api/local-auth/testing-rate");
        Assert.Equal(400, completion.State["StatusCode"]);
        Assert.Equal(LogLevel.Warning, completion.Level);
        Assert.Equal("antiforgery.trace-123", completion.Scope["TraceId"]);
    }

    [Fact]
    public async Task DisabledLocalAuthLogsFinalNotFoundAtWarning()
    {
        using var certificate = TestDataProtectionCertificate.CreateRsa();
        await using var production = factory.WithWebHostBuilder(
            certificate.ConfigureProductionHost);
        var logs = production.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(
            CorrelationIdMiddleware.HeaderName,
            "local-disabled.trace-123");

        using var response = await client.GetAsync(
            "/api/local-auth/testing",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var completion = FindCompletion(logs, "/api/local-auth/testing");
        Assert.Equal(404, completion.State["StatusCode"]);
        Assert.Equal(LogLevel.Warning, completion.Level);
        Assert.Equal("local-disabled.trace-123", completion.Scope["TraceId"]);
    }

    [Fact]
    public async Task UnhandledExceptionLogContainsOnlySafeOperationalFields()
    {
        const string sensitiveMessage = "sensitive-database-message";
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Add(
            CorrelationIdMiddleware.HeaderName,
            "fault.trace-123");

        using var response = await client.GetAsync(
            "/api/testing/fault?secret=query-value",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(sensitiveMessage, body, StringComparison.OrdinalIgnoreCase);
        var error = Assert.Single(
            logs.Logs,
            log => log.Category.EndsWith("ApiExceptionHandler", StringComparison.Ordinal) &&
                   log.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.Equal(
            typeof(InvalidOperationException).FullName,
            error.State["ExceptionType"]);
        Assert.Equal("/api/testing/fault", error.State["Path"]);
        Assert.Equal("fault.trace-123", error.State["TraceId"]);
        Assert.Equal("fault.trace-123", error.Scope["TraceId"]);
        Assert.DoesNotContain(sensitiveMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("query-value", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            error.State.Values,
            value => value?.ToString()?.Contains(
                sensitiveMessage,
                StringComparison.OrdinalIgnoreCase) is true);
        Assert.DoesNotContain(
            logs.Logs,
            log =>
                log.Message.Contains(
                    sensitiveMessage,
                    StringComparison.OrdinalIgnoreCase) ||
                log.State.Values.Any(
                    value => value?.ToString()?.Contains(
                        sensitiveMessage,
                        StringComparison.OrdinalIgnoreCase) is true) ||
                log.Exception?.ToString().Contains(
                    sensitiveMessage,
                    StringComparison.OrdinalIgnoreCase) is true);
    }

    private static CapturedLog FindCompletion(
        CapturedLogProvider logs,
        string path) =>
        Assert.Single(
            logs.Logs,
            log => log.Category.EndsWith(
                       nameof(RequestLoggingMiddleware),
                       StringComparison.Ordinal) &&
                   log.State.TryGetValue("Path", out var actualPath) &&
                   Equals(actualPath, path));

    private static bool CapturedRenderingContains(
        CapturedLog log,
        string sensitiveValue) =>
        new[] { log.Category, log.Message }
            .Concat(log.State.Values.Select(value =>
                value?.ToString() ?? string.Empty))
            .Concat(log.Scope.Values.Select(value =>
                value?.ToString() ?? string.Empty))
            .Append(log.Exception?.ToString() ?? string.Empty)
            .Any(value => value.Contains(
                sensitiveValue,
                StringComparison.OrdinalIgnoreCase));

    private sealed record ProblemTrace(string TraceId);
}
