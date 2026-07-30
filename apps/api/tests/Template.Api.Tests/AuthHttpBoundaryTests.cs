using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task SuccessfulLocalAuthProbeIsNotCacheable()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/local-auth/testing",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task E2EHarnessCanModelHttpsBoundaryInTestEnvironment()
    {
        await using var e2e = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Testing:AssumeHttpsBoundary"] = "true"
                })));
        using var client = e2e.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });

        using var response = await client.GetAsync(
            "/api/v1/auth/csrf",
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
        using var certificate = TestDataProtectionCertificate.CreateRsa();
        await using var production = factory.WithWebHostBuilder(
            certificate.ConfigureProductionHost);
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync(
            "/api/local-auth/testing",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
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
        Assert.Contains("no-store", second.Headers.CacheControl?.ToString());
        Assert.True(second.Headers.RetryAfter is not null);
        var problem = await second.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("rate_limited", problem!.Code);
    }

    [Fact]
    public async Task CreateLimiterSeparatesClientsBehindTrustedLoopbackProxy()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "1"
                }));
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
        });
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var csrf = await client.GetFromJsonAsync<CsrfToken>(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);

        using var firstClient = await SendProxiedProtectedPost(
            client,
            csrf!.RequestToken,
            "198.51.100.10");
        using var secondClient = await SendProxiedProtectedPost(
            client,
            csrf.RequestToken,
            "198.51.100.11");
        using var repeatedFirstClient = await SendProxiedProtectedPost(
            client,
            csrf.RequestToken,
            "198.51.100.10");

        Assert.Equal(HttpStatusCode.OK, firstClient.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondClient.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            repeatedFirstClient.StatusCode);
    }

    [Fact]
    public async Task CreateLimiterIgnoresForwardedClientFromUntrustedPeer()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "1"
                }));
            builder.ConfigureServices(services =>
                services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>());
        });
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        var csrf = await client.GetFromJsonAsync<CsrfToken>(
            "/api/testing/csrf",
            TestContext.Current.CancellationToken);

        using var first = await SendProxiedProtectedPost(
            client,
            csrf!.RequestToken,
            "198.51.100.10",
            "203.0.113.50");
        using var spoofedSecondClient = await SendProxiedProtectedPost(
            client,
            csrf.RequestToken,
            "198.51.100.11",
            "203.0.113.50");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            spoofedSecondClient.StatusCode);
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

    private static Task<HttpResponseMessage> SendProxiedProtectedPost(
        HttpClient client,
        string token,
        string forwardedFor,
        string? remoteIp = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/testing-rate");
        request.Headers.Add("X-CSRF-TOKEN", token);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add(
            "X-Testing-Remote-IP",
            remoteIp ?? IPAddress.Loopback.ToString());
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next) =>
            application =>
            {
                application.Use(async (context, nextMiddleware) =>
                {
                    if (IPAddress.TryParse(
                            context.Request.Headers["X-Testing-Remote-IP"],
                            out var remoteIp))
                    {
                        context.Connection.RemoteIpAddress = remoteIp;
                    }

                    await nextMiddleware();
                });
                next(application);
            };
    }

    private sealed record CsrfToken(string RequestToken);
    private sealed record ApiProblem(string Code);
}
