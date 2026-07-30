using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Endpoints;
using Template.Api.Errors;
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

    public static TheoryData<string, HttpStatusCode, string> IncompatibleAcceptErrorCases =>
        new()
        {
            {
                "/api/v1/system/status?echo=" + new string('x', 65),
                HttpStatusCode.BadRequest,
                "validation_failed"
            },
            { "/api/v1/system/authenticated", HttpStatusCode.Unauthorized, "unauthorized" },
            { "/api/does-not-exist", HttpStatusCode.NotFound, "not_found" },
            { "/api/testing/fault", HttpStatusCode.InternalServerError, "internal_error" },
        };

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ApiFailuresUseStableProblemDetails(
        HttpMethod method,
        string uri,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var client = factory.CreateApiClient();
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

    [Theory]
    [MemberData(nameof(IncompatibleAcceptErrorCases))]
    public async Task ApiFailuresIgnoreIncompatibleAcceptHeader(
        string uri,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("text/plain");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem.Code);
        Assert.False(string.IsNullOrWhiteSpace(problem.TraceId));
    }

    [Fact]
    public async Task ValidationFailureUsesCamelCaseFieldErrors()
    {
        using var client = factory.CreateApiClient();

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
    public async Task NestedValidationKeysUseJsonPathsAndMergeCollisions()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/testing/nested-validation",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.TryGetValue("address.postalCode", out var postalCodeMessages));
        Assert.Equal(2, postalCodeMessages.Length);
        Assert.Contains("Postal code is required.", postalCodeMessages);
        Assert.Contains("Postal code has an invalid format.", postalCodeMessages);
        Assert.True(problem.Errors.ContainsKey("contactInfo.emailAddress"));
        Assert.DoesNotContain("address.PostalCode", problem.Errors.Keys);
    }

    [Fact]
    public async Task AuthenticatedPrincipalWithoutRequiredClaimGetsForbiddenProblem()
    {
        using var client = factory.CreateApiClient();
        using var scenario = await LocalAuthTestClient.CreateScenarioAsync(client);
        Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);

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
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/testing/fault",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("sensitive-database-message", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProblemExceptionCopiesOnlyAllowListedSafeExtensions()
    {
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IEndpointModule, ExtensionProblemModule>()));
        using var client = isolated.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });

        using var response = await client.GetAsync(
            "/api/testing/problem-extensions",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var problem = document.RootElement;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "member_domain_acknowledgement_required",
            problem.GetProperty("code").GetString());
        Assert.Equal(
            "target@example.test",
            problem.GetProperty("email").GetString());
        Assert.Equal(
            "example.test",
            problem.GetProperty("emailDomain").GetString());
        Assert.Equal(
            ["allowed.example"],
            problem.GetProperty("allowedEmailDomains")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray());
        Assert.False(problem.TryGetProperty("secret", out _));
        Assert.False(problem.TryGetProperty("nested", out _));
        Assert.False(problem.TryGetProperty("wrongArray", out _));
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

    private sealed class ExtensionProblemModule : IEndpointModule
    {
        public void MapEndpoints(EndpointRouteContext context)
        {
            context.Root.MapGet(
                    "/api/testing/problem-extensions",
                    ThrowExtensionProblem)
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        private static IResult ThrowExtensionProblem() =>
            throw new ApiProblemException(
                StatusCodes.Status409Conflict,
                "member_domain_acknowledgement_required",
                new Dictionary<string, object?>
                {
                    ["email"] = "target@example.test",
                    ["emailDomain"] = "example.test",
                    ["allowedEmailDomains"] =
                        new[] { "allowed.example" },
                    ["secret"] = "must-not-leak",
                    ["nested"] = new { Unsafe = true },
                    ["wrongArray"] = new[] { 1, 2 }
                });
    }
}
