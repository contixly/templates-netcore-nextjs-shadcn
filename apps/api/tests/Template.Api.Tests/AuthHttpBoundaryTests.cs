using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class AuthHttpBoundaryTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task MissingAntiforgeryTokenUsesStableProblem()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(
            "/api/local-auth/testing-rate",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("antiforgery_failed", problem!.Code);
    }

    [Fact]
    public async Task ValidHeaderAndHttpOnlySecureAntiforgeryCookieAreAccepted()
    {
        using var client = factory.CreateApiClient();
        using var csrf = await client.GetAsync(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);
        var token = await csrf.Content.ReadFromJsonAsync<CsrfToken>(
            TestContext.Current.CancellationToken);
        var setCookie = csrf.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("__Host-template.antiforgery=", setCookie);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/testing/csrf");
        request.Headers.Add("X-CSRF-TOKEN", token!.RequestToken);
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidFormTokenWithoutRequiredHeaderIsRejected()
    {
        using var client = factory.CreateApiClient();
        var csrf = await client.GetFromJsonAsync<CsrfToken>(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);
        using var form = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>(
                "__RequestVerificationToken",
                csrf!.RequestToken)
        ]);

        using var response = await client.PostAsync(
            "/api/testing/csrf",
            form,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("antiforgery_failed", problem!.Code);
    }

    [Fact]
    public async Task ProductionLocalRouteAlwaysReturnsLocalDisabledProblem()
    {
        await using var production = factory.WithWebHostBuilder(
            builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(
            "/api/local-auth/testing",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("local_auth_disabled", problem!.Code);
    }

    [Fact]
    public async Task CreateLimiterReturnsTyped429AndRetryAfter()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "1"
                })));
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var csrf = await client.GetFromJsonAsync<CsrfToken>(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);

        using var first = await SendProtectedPost(client, csrf!.RequestToken);
        using var second = await SendProtectedPost(client, csrf.RequestToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.RetryAfter is not null);
        var problem = await second.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("rate_limited", problem!.Code);
    }

    private static Task<HttpResponseMessage> SendProtectedPost(
        HttpClient client,
        string token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/testing-rate");
        request.Headers.Add("X-CSRF-TOKEN", token);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record CsrfToken(string RequestToken);
    private sealed record ApiProblem(string Code);
}
