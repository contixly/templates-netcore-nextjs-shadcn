using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class LocalAutomationConfirmationTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LocalAutomationConfirmationUpdatesEmailAndRenewsCurrentTicket()
    {
        using var client = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Confirmation User",
                email = "local-agent+confirmation@local-agent.test",
                password = "local-confirmation-password"
            });
        var scenario = await created.Content.ReadFromJsonAsync<
            LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        var before = await client.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        Assert.False(before!.Data.User!.EmailVerified);
        byte[] protectedTicket;
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            protectedTicket = await beforeScope.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Sessions
                .Where(session => session.Id == before.Data.Session!.Id)
                .Select(session => session.ProtectedTicket)
                .SingleAsync(TestContext.Current.CancellationToken);
        }
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await LocalAuthTestClient.ConfirmEmailAsync(client);
        var confirmed = await response.Content.ReadFromJsonAsync<SessionEnvelope>(
            TestContext.Current.CancellationToken);
        var current = await client.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(confirmed!.Data.User!.EmailVerified);
        Assert.True(current!.Data.User!.EmailVerified);
        Assert.Equal(before.Data.Session!.Id, confirmed.Data.Session!.Id);
        Assert.Equal(before.Data.Session.Id, current.Data.Session!.Id);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.True(await db.Users
            .Where(user => user.Id == scenario!.Data.User.Id)
            .Select(user => user.EmailConfirmed)
            .SingleAsync(TestContext.Current.CancellationToken));
        var renewedTicket = await db.Sessions
            .Where(session => session.Id == before.Data.Session.Id)
            .Select(session => session.ProtectedTicket)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(protectedTicket, renewedTicket);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var audit = Assert.Single(
            logs.Logs,
            log => log.Category ==
                "Template.Api.Features.Auth.AuthEndpointModule");
        Assert.Equal("local_email_confirm", audit.State["AuthOperation"]);
        Assert.Equal("succeeded", audit.State["AuthOutcome"]);
        var rendered = string.Join(
            " ",
            new[] { audit.Message }
                .Concat(audit.State.Values.Select(value =>
                    value?.ToString() ?? string.Empty)));
        Assert.DoesNotContain(scenario!.Data.Email, rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalAutomationConfirmationRequiresAuthenticationAndCsrf()
    {
        using var anonymous = factory.CreateApiClient();
        using var anonymousResponse = await LocalAuthTestClient
            .ConfirmEmailAsync(anonymous);
        await AssertProblemAsync(
            anonymousResponse,
            HttpStatusCode.Unauthorized,
            "unauthorized");

        using var authenticated = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            authenticated);
        using var missingCsrf = await authenticated.PostAsync(
            "/api/local-auth/confirm-email",
            content: null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            missingCsrf,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
    }

    [Fact]
    public async Task LocalAutomationConfirmationRejectsNonLocalCurrentUser()
    {
        using var client = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(client);
        var scenario = await created.Content.ReadFromJsonAsync<
            LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
                .Users
                .Where(user => user.Id == scenario!.Data.User.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.IsLocalAutomation,
                        false),
                    TestContext.Current.CancellationToken);
        }

        using var response = await LocalAuthTestClient.ConfirmEmailAsync(client);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "local_auth_user_required");
    }

    [Fact]
    public async Task DisabledAndProductionHostsAlwaysHideLocalAutomationConfirmation()
    {
        await using var disabled = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:Enabled"] = "false"
                    })));
        using var disabledClient = disabled.CreateClient(ClientOptions());
        using var disabledResponse = await disabledClient.PostAsync(
            "/api/local-auth/confirm-email",
            content: null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            disabledResponse,
            HttpStatusCode.NotFound,
            "local_auth_disabled");

        using var certificate = TestDataProtectionCertificate.CreateRsa();
        await using var production = factory.WithWebHostBuilder(
            certificate.ConfigureProductionHost);
        using var productionClient = production.CreateClient(ClientOptions());
        using var productionResponse = await productionClient.PostAsync(
            "/api/local-auth/confirm-email",
            content: null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            productionResponse,
            HttpStatusCode.NotFound,
            "local_auth_disabled");
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    };

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal(code, problem!.Code);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    private sealed record ApiProblem(string Code);
    private sealed record SessionEnvelope(SessionData Data);
    private sealed record SessionData(
        bool Authenticated,
        UserData? User,
        SessionMetadata? Session);
    private sealed record UserData(Guid Id, bool EmailVerified);
    private sealed record SessionMetadata(Guid Id);
}
