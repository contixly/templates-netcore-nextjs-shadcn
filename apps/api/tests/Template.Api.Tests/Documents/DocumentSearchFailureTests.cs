using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Api.Tests.Infrastructure;
using Template.Application.Documents;
using Template.Application.Documents.Ports;

namespace Template.Api.Tests.Documents;

public sealed class DocumentSearchFailureTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task ProviderFailureReturnsSafeProblemAndSafeLogs()
    {
        const string sensitive = "sensitive fixture text";
        const string query = "secret-query-source-body";
        await using var failingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDocumentSearchIndexProvider>();
                services.AddSingleton<IDocumentSearchIndexProvider>(
                    new ThrowingProvider(sensitive));
            }));
        var logs = failingFactory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = failingFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = false
            });

        using var response = await client.GetAsync(
            $"/api/v1/documents-system/search?q={query}&locale=en",
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(500, problem!.Status);
        Assert.Equal("urn:template:problem:internal_error", problem.Type);
        Assert.Equal("Internal server error", problem.Title);
        Assert.Equal("/api/v1/documents-system/search", problem.Instance);
        Assert.Equal("internal_error", problem.Extensions["code"]!.ToString());
        Assert.False(string.IsNullOrWhiteSpace(problem.Extensions["traceId"]!.ToString()));
        Assert.DoesNotContain(sensitive, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(query, payload, StringComparison.Ordinal);

        var serializedLogs = string.Join(
            '\n',
            logs.Logs.Select(log =>
                $"{log.Message} {string.Join(' ', log.State.Values)} " +
                $"{string.Join(' ', log.Scope.Values)}"));
        Assert.DoesNotContain(sensitive, serializedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(query, serializedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("Template.Documents", serializedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("body", serializedLogs, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingProvider(string message) : IDocumentSearchIndexProvider
    {
        public DocumentSearchLocaleIndex Get(DocumentLocale locale) =>
            throw new InvalidOperationException(message);
    }
}
