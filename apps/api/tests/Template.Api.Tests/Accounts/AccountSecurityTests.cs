using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Api.Tests.Accounts;

public sealed class AccountSecurityTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);
        factory.Services.GetRequiredService<CapturedLogProvider>().Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("GET", "/api/v1/account")]
    [InlineData("PATCH", "/api/v1/account/profile")]
    [InlineData("GET", "/api/v1/account/connections")]
    [InlineData("DELETE", "/api/v1/account/connections/google")]
    [InlineData("GET", "/api/v1/account/sessions")]
    [InlineData("DELETE", "/api/v1/account/sessions/0198776b-6210-7e54-82d8-19fb35683550")]
    [InlineData("DELETE", "/api/v1/account/sessions/others")]
    [InlineData("DELETE", "/api/v1/account")]
    public async Task EveryAccountRouteRequiresBrowserSessionAndIsNeverCached(
        string method,
        string path)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content = method == "PATCH" || path == "/api/v1/account"
                    ? JsonContent.Create(new { })
                    : null
            },
            TestContext.Current.CancellationToken);
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", problem.Code);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("PATCH", "/api/v1/account/profile", true)]
    [InlineData("DELETE", "/api/v1/account/connections/google", false)]
    [InlineData("DELETE", "/api/v1/account/sessions/0198776b-6210-7e54-82d8-19fb35683550", false)]
    [InlineData("DELETE", "/api/v1/account/sessions/others", false)]
    [InlineData("DELETE", "/api/v1/account", true)]
    public async Task EveryAccountMutationRequiresAntiforgery(
        string method,
        string path,
        bool hasBody)
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Csrf Owner",
            "local-agent+account-csrf@local-agent.test");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (hasBody)
        {
            request.Content = JsonContent.Create(new
            {
                displayName = "Changed Name",
                confirmationEmail = "local-agent+account-csrf@local-agent.test"
            });
        }

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("antiforgery_failed", problem.Code);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AccountAuditEventsContainSafeBoundedContextOnly()
    {
        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ExternalAuthentication:PublicOrigin"] =
                            "https://localhost",
                        ["ExternalAuthentication:Providers:Google:ClientId"] =
                            "audit-google-client",
                        ["ExternalAuthentication:Providers:Google:ClientSecret"] =
                            "audit-google-secret",
                        ["ExternalAuthentication:Providers:GitHub:ClientId"] =
                            "audit-github-client",
                        ["ExternalAuthentication:Providers:GitHub:ClientSecret"] =
                            "audit-github-secret"
                    })));
        using var client = configuredFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Audit Owner",
            "local-agent+audit-owner@local-agent.test",
            "local-audit-owner-password");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            configuredFactory.Services,
            scenario.UserId,
            "google",
            "sensitive-provider-subject-771");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            configuredFactory.Services,
            scenario.UserId,
            "github",
            "sensitive-provider-subject-772");
        using var other = configuredFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        using var signIn = await LocalAuthTestClient.SignInAsync(
            other,
            scenario.Email,
            scenario.Password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        configuredFactory.Services
            .GetRequiredService<CapturedLogProvider>()
            .Clear();

        using var profile = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/account/profile",
            new { displayName = "Audit Renamed" });
        var otherSessionId = await GetOtherSessionIdAsync(
            scenario.UserId,
            configuredFactory.Services);
        using var revoke = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/account/sessions/{otherSessionId}");
        using var revokeOthers = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account/sessions/others");
        using var disconnect = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account/connections/google");
        using var delete = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account",
            new { confirmationEmail = scenario.Email });

        Assert.All(
            new[] { profile, revoke, revokeOthers, disconnect, delete },
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var accountEvents = configuredFactory.Services
            .GetRequiredService<CapturedLogProvider>()
            .Logs
            .Where(log =>
                log.Category ==
                "Template.Api.Features.Account.AccountEndpointModule")
            .ToArray();
        Assert.Contains(
            accountEvents,
            log => Equals(log.State.GetValueOrDefault("AccountOperation"), "profile_update"));
        Assert.Contains(
            accountEvents,
            log => Equals(log.State.GetValueOrDefault("AccountOperation"), "session_revoke"));
        Assert.Contains(
            accountEvents,
            log => Equals(log.State.GetValueOrDefault("AccountOperation"), "sessions_revoke_others"));
        Assert.Contains(
            accountEvents,
            log => Equals(log.State.GetValueOrDefault("AccountOperation"), "provider_disconnect"));
        Assert.Contains(
            accountEvents,
            log => Equals(log.State.GetValueOrDefault("AccountOperation"), "account_delete"));
        Assert.All(
            accountEvents,
            log =>
            {
                Assert.Equal("succeeded", log.State["AccountOutcome"]);
                Assert.Equal(scenario.UserId, log.State["UserId"]);
                Assert.True(log.Scope.ContainsKey("TraceId"));
            });
        var rendered = string.Join(
            Environment.NewLine,
            accountEvents.Select(log => log.Message));
        Assert.DoesNotContain(scenario.Email, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive-provider-subject", rendered);
        Assert.DoesNotContain("local-audit-owner-password", rendered);
        Assert.DoesNotContain("__Host-template.session", rendered);
        Assert.DoesNotContain("ticket", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PerTestResetRemovesAuthRowsButPreservesSharedDataProtectionKeys()
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Reset Owner",
            "local-agent+reset-owner@local-agent.test");
        int keysBefore;
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var before = beforeScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            before.OpenIddictTokens.Add(new OpenIddictEntityFrameworkCoreToken
            {
                Id = "account-reset-state-token",
                CreationDate = DateTimeOffset.UtcNow.UtcDateTime,
                ExpirationDate = DateTimeOffset.UtcNow.AddMinutes(5).UtcDateTime,
                Status = Statuses.Valid,
                Type = TokenTypeIdentifiers.Private.StateToken
            });
            await before.SaveChangesAsync(TestContext.Current.CancellationToken);
            keysBefore = await before.DataProtectionKeys.CountAsync(
                TestContext.Current.CancellationToken);
            Assert.True(keysBefore > 0);
        }

        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

        await using var afterScope = factory.Services.CreateAsyncScope();
        var after = afterScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.Equal(
            keysBefore,
            await after.DataProtectionKeys.CountAsync(
                TestContext.Current.CancellationToken));
        Assert.False(await after.OpenIddictTokens.AnyAsync(
            TestContext.Current.CancellationToken));
        Assert.False(await after.UserLogins.AnyAsync(
            TestContext.Current.CancellationToken));
        Assert.False(await after.UserEmails.AnyAsync(
            TestContext.Current.CancellationToken));
        Assert.False(await after.Sessions.AnyAsync(
            TestContext.Current.CancellationToken));
        Assert.False(await after.Users.AnyAsync(
            TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> GetOtherSessionIdAsync(
        Guid userId,
        IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var sessions = await scope.ServiceProvider
            .GetRequiredService<TemplateDbContext>()
            .Sessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.CreatedAt)
            .Select(session => session.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, sessions.Length);
        return sessions[0];
    }
}
