using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;

namespace Template.Api.Tests;

public sealed class SystemEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task PublicStatusReturnsTypedDataEnvelope()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/system/status?echo=hello",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SystemStatusEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Data.Status);
        Assert.Equal("1", payload.Data.ApiVersion);
        Assert.Equal("hello", payload.Data.Echo);
        Assert.NotEqual(default, payload.Data.Timestamp);
    }

    [Fact]
    public async Task ProtectedProbeRejectsAnonymousRequest()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/system/authenticated",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedProbeAcceptsTestAuthenticatedRequest()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeaderName, "user-1");

        using var response = await client.GetAsync(
            "/api/v1/system/authenticated",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthenticatedEnvelope>(
            TestContext.Current.CancellationToken);
        Assert.Equal("authenticated", payload!.Data.Status);
    }

    [Fact]
    public async Task VersionedConsumerRoutesAreProtectedByDefault()
    {
        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            "/api/v1/testing/consumer",
            TestContext.Current.CancellationToken);

        using var authenticatedClient = factory.CreateClient();
        authenticatedClient.DefaultRequestHeaders.Add(
            TestAuthenticationHandler.UserHeaderName,
            "user-1");
        using var authenticatedResponse = await authenticatedClient.GetAsync(
            "/api/v1/testing/consumer",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public void ProductionCookieUsesHostPrefixSecurityRequirements()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(ApiAuthenticationDefaults.SchemeName);

        Assert.Equal("__Host-template.session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
    }

    private sealed record SystemStatusEnvelope(SystemStatusData Data);
    private sealed record SystemStatusData(
        string Status,
        string ApiVersion,
        DateTimeOffset Timestamp,
        string? Echo);
    private sealed record AuthenticatedEnvelope(AuthenticatedData Data);
    private sealed record AuthenticatedData(string Status);
}
