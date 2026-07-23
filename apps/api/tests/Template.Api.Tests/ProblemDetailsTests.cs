using System.Net;
using System.Net.Http.Json;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class ProblemDetailsTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    public static TheoryData<HttpMethod, string, HttpStatusCode, string> ErrorCases =>
        new()
        {
            {
                HttpMethod.Get,
                "/api/v1/system/status?echo=" + new string('x', 65),
                HttpStatusCode.BadRequest,
                "validation_failed"
            },
            { HttpMethod.Get, "/api/v1/system/authenticated", HttpStatusCode.Unauthorized, "unauthorized" },
            { HttpMethod.Get, "/api/does-not-exist", HttpStatusCode.NotFound, "not_found" },
            { HttpMethod.Post, "/api/v1/system/status", HttpStatusCode.MethodNotAllowed, "method_not_allowed" },
            { HttpMethod.Get, "/api/testing/fault", HttpStatusCode.InternalServerError, "internal_error" },
        };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ApiFailuresUseStableProblemDetails(
        HttpMethod method,
        string uri,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method, uri);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Code);
        Assert.Equal($"urn:template:problem:{expectedCode}", problem.Type);
        Assert.Equal(uri.Split('?', 2)[0], problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task ValidationFailureUsesCamelCaseFieldErrors()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/system/status?echo=" + new string('x', 65),
            TestContext.Current.CancellationToken);

        var problem = await response.Content.ReadFromJsonAsync<ValidationApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("validation_failed", problem!.Code);
        Assert.True(problem.Errors.TryGetValue("echo", out var messages));
        Assert.Contains(
            "The field echo must be between 1 and 64 characters.",
            messages);
    }

    [Fact]
    public async Task AuthenticatedPrincipalWithoutRequiredClaimGetsForbiddenProblem()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeaderName, "user-1");

        using var response = await client.GetAsync(
            "/api/testing/forbidden",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("forbidden", problem!.Code);
    }

    [Fact]
    public async Task UnhandledExceptionDoesNotExposeExceptionMessage()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/testing/fault",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("sensitive-database-message", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
    }

    private record ApiProblem(
        string Type,
        string Title,
        int Status,
        string Detail,
        string Instance,
        string Code,
        string TraceId);

    private sealed record ValidationApiProblem(
        string Type,
        string Title,
        int Status,
        string Detail,
        string Instance,
        string Code,
        string TraceId,
        Dictionary<string, string[]> Errors)
        : ApiProblem(Type, Title, Status, Detail, Instance, Code, TraceId);
}
