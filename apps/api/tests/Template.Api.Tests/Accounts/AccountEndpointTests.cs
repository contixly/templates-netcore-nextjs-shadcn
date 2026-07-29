using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.Accounts;

public sealed class AccountEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AccountReadProjectsOnlySafeAccountAndVerifiedEmailFields()
    {
        using var client = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Account Owner",
            "local-agent+account-read@local-agent.test");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Users
                .Where(user => user.Id == scenario.UserId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.ImageUrl,
                        "http://unsafe.example.test/avatar.png"),
                    TestContext.Current.CancellationToken);
        }

        using var response = await client.GetAsync(
            "/api/v1/account",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var root = document.RootElement;
        var data = root.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(["data"], AccountEndpointTestSupport.PropertyNames(root));
        Assert.Equal(
            [
                "id",
                "displayName",
                "primaryEmail",
                "imageUrl",
                "createdAt",
                "verifiedEmails"
            ],
            AccountEndpointTestSupport.PropertyNames(data));
        Assert.Equal(scenario.UserId, data.GetProperty("id").GetGuid());
        Assert.Equal("Account Owner", data.GetProperty("displayName").GetString());
        Assert.Equal(
            "local-agent+account-read@local-agent.test",
            data.GetProperty("primaryEmail").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("imageUrl").ValueKind);
        var email = Assert.Single(data.GetProperty("verifiedEmails").EnumerateArray());
        Assert.Equal(
            ["email", "isPrimary", "providers"],
            AccountEndpointTestSupport.PropertyNames(email));
        Assert.True(email.GetProperty("isPrimary").GetBoolean());
        Assert.Empty(email.GetProperty("providers").EnumerateArray());
        Assert.DoesNotContain(
            "password",
            root.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "hash",
            root.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxy")]
    [InlineData("valid\u0007name")]
    public async Task ProfileUpdateValidatesNormalizedDisplayName(string displayName)
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Profile Owner",
            "local-agent+profile-validation@local-agent.test");

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/account/profile",
            new { displayName });
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem.Code);
        Assert.Contains("displayName", problem.Errors!.Keys);
    }

    [Theory]
    [InlineData("{}", "application/json")]
    [InlineData("{\"displayName\":\"Valid Name\",\"imageUrl\":\"https://pii.example/avatar\"}", "application/json")]
    [InlineData("{\"displayName\":", "application/json")]
    [InlineData("{\"displayName\":\"Valid Name\"}", "text/plain")]
    public async Task ProfileUpdateRejectsMissingUnmappedMalformedOrNonJsonBodies(
        string body,
        string mediaType)
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Strict Profile",
            "local-agent+strict-profile@local-agent.test");

        using var response = await AccountEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/account/profile",
            body,
            mediaType);
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            problem.Code,
            new[] { "invalid_request", "validation_failed" });
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ProfileUpdateTrimsAndReturnsTheUpdatedAccountEnvelope()
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Before Name",
            "local-agent+profile-success@local-agent.test");

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/account/profile",
            new { displayName = "  After Name  " });
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(
            "After Name",
            document.RootElement
                .GetProperty("data")
                .GetProperty("displayName")
                .GetString());
    }

    [Fact]
    public async Task ProfileUpdateRaceWithAccountDeletionReturnsUnauthorizedWithoutSuccessAudit()
    {
        var store = new RacingAccountStore(profileMissing: true);
        await using var racingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountStore>();
                services.AddSingleton<IAccountStore>(store);
            }));
        using var client = racingFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Profile Race",
            "local-agent+profile-race@local-agent.test");
        racingFactory.Services.GetRequiredService<CapturedLogProvider>().Clear();

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Patch,
            "/api/v1/account/profile",
            new { displayName = "Deleted Concurrently" });
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);
        var audit = Assert.Single(
            racingFactory.Services
            .GetRequiredService<CapturedLogProvider>()
            .Logs,
            log =>
                log.Category ==
                "Template.Api.Features.Account.AccountEndpointModule");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", problem.Code);
        Assert.Equal("profile_update", audit.State["AccountOperation"]);
        Assert.Equal("unauthorized", audit.State["AccountOutcome"]);
        Assert.DoesNotContain(
            "succeeded",
            audit.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectionsContainConfiguredAndExistingProvidersWithoutSubjects()
    {
        await using var configuredFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ExternalAuthentication:PublicOrigin"] =
                            "https://localhost",
                        ["ExternalAuthentication:Providers:GitHub:ClientId"] =
                            "account-test-client",
                        ["ExternalAuthentication:Providers:GitHub:ClientSecret"] =
                            "account-test-secret"
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
            "Connection Owner",
            "local-agent+connections@local-agent.test");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            configuredFactory.Services,
            scenario.UserId,
            "google",
            "subject-must-not-leak");

        using var response = await client.GetAsync(
            "/api/v1/account/connections",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var items = document.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();
        var github = Assert.Single(
            items,
            item => item.GetProperty("provider").GetString() == "github");
        var google = Assert.Single(
            items,
            item => item.GetProperty("provider").GetString() == "google");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, items.Length);
        Assert.True(github.GetProperty("configured").GetBoolean());
        Assert.False(github.GetProperty("connected").GetBoolean());
        Assert.True(github.GetProperty("canConnect").GetBoolean());
        Assert.False(google.GetProperty("configured").GetBoolean());
        Assert.True(google.GetProperty("connected").GetBoolean());
        Assert.False(google.GetProperty("isCurrentAuthenticationMethod").GetBoolean());
        Assert.False(google.GetProperty("canDisconnect").GetBoolean());
        Assert.Equal(
            "external_connection_required",
            google.GetProperty("disabledReason").GetString());
        Assert.DoesNotContain(
            "subject-must-not-leak",
            document.RootElement.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "providerSubject",
            document.RootElement.ToString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "token",
            document.RootElement.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisconnectUsesStableMissingAndSafetyConflictProblems()
    {
        using var client = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Disconnect Conflict",
            "local-agent+disconnect-conflict@local-agent.test");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            factory.Services,
            scenario.UserId,
            "google",
            "disconnect-conflict");

        using var conflict = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account/connections/google");
        using var missing = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account/connections/github");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(
            "external_connection_required",
            (await AccountEndpointTestSupport.ReadProblemAsync(conflict)).Code);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            "external_connection_not_found",
            (await AccountEndpointTestSupport.ReadProblemAsync(missing)).Code);
    }

    [Fact]
    public async Task DisconnectReturnsProviderAndRemovesOnlyTheSelectedConnection()
    {
        using var client = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Disconnect Success",
            "local-agent+disconnect-success@local-agent.test");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            factory.Services,
            scenario.UserId,
            "google",
            "disconnect-google");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            factory.Services,
            scenario.UserId,
            "github",
            "disconnect-github");

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account/connections/google");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "google",
            document.RootElement
                .GetProperty("data")
                .GetProperty("provider")
                .GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.False(await db.UserLogins.AnyAsync(
            login =>
                login.UserId == scenario.UserId &&
                login.LoginProvider == "google",
            TestContext.Current.CancellationToken));
        Assert.True(await db.UserLogins.AnyAsync(
            login =>
                login.UserId == scenario.UserId &&
                login.LoginProvider == "github",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CurrentAuthenticationMethodCannotBeDisconnected()
    {
        var useProviderMethod = false;
        await using var providerSessionFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<CookieAuthenticationOptions>(
                    BrowserSessionAuthenticationDefaults.PrimaryScheme,
                    options =>
                    {
                        options.Events.OnValidatePrincipal = async context =>
                        {
                            if (!useProviderMethod)
                            {
                                return;
                            }

                            var db = context.HttpContext.RequestServices
                                .GetRequiredService<AuthDbContext>();
                            await db.Sessions.ExecuteUpdateAsync(
                                setters => setters.SetProperty(
                                    session => session.AuthenticationMethod,
                                    "google"),
                                context.HttpContext.RequestAborted);
                        };
                    })));
        using var client = providerSessionFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Current Provider",
            "local-agent+current-provider@local-agent.test");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            providerSessionFactory.Services,
            scenario.UserId,
            "google",
            "current-google");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            providerSessionFactory.Services,
            scenario.UserId,
            "github",
            "other-github");
        var csrf = await LocalAuthTestClient.GetCsrfAsync(client);
        useProviderMethod = true;

        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/v1/account/connections/google");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("external_connection_required", problem.Code);
    }

    [Theory]
    [InlineData(
        DisconnectTerminalState.Removed,
        HttpStatusCode.NotFound,
        "external_connection_not_found")]
    [InlineData(
        DisconnectTerminalState.Unsafe,
        HttpStatusCode.Conflict,
        "external_connection_required")]
    [InlineData(
        DisconnectTerminalState.StillSafe,
        HttpStatusCode.Conflict,
        "concurrency_conflict")]
    public async Task ExhaustedDisconnectContentionReclassifiesTheReadOnlyFinalState(
        DisconnectTerminalState terminalState,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        var store = new RacingAccountStore(terminalState: terminalState);
        await using var racingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountStore>();
                services.AddSingleton<IAccountStore>(store);
            }));
        using var client = racingFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            $"Disconnect {terminalState}",
            $"local-agent+disconnect-{terminalState.ToString().ToLowerInvariant()}@local-agent.test");
        racingFactory.Services.GetRequiredService<CapturedLogProvider>().Clear();

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account/connections/github");
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);
        var audit = Assert.Single(
            racingFactory.Services
            .GetRequiredService<CapturedLogProvider>()
            .Logs,
            log =>
                log.Category ==
                "Template.Api.Features.Account.AccountEndpointModule");

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedCode, problem.Code);
        Assert.Equal(3, store.DisconnectSnapshotReads);
        Assert.Equal(2, store.DisconnectAttempts);
        Assert.Equal(expectedCode, audit.State["AccountOutcome"]);
    }

    [Fact]
    public async Task SessionsAreDeterministicallyPagedAndRedactNetworkAddresses()
    {
        using var first = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            first,
            "Session Owner",
            "local-agent+session-page@local-agent.test",
            "local-session-page-password");
        using var second = factory.CreateApiClient();
        using var secondSignIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario.Email,
            scenario.Password);
        Assert.Equal(HttpStatusCode.OK, secondSignIn.StatusCode);
        using var third = factory.CreateApiClient();
        using var thirdSignIn = await LocalAuthTestClient.SignInAsync(
            third,
            scenario.Email,
            scenario.Password);
        Assert.Equal(HttpStatusCode.OK, thirdSignIn.StatusCode);
        await AccountEndpointTestSupport.SetSessionMetadataAsync(factory.Services);

        using var firstPage = await first.GetAsync(
            "/api/v1/account/sessions?limit=2",
            TestContext.Current.CancellationToken);
        using var firstDocument = JsonDocument.Parse(
            await firstPage.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var firstData = firstDocument.RootElement.GetProperty("data");
        var firstItems = firstData.GetProperty("items").EnumerateArray().ToArray();
        var cursor = firstData.GetProperty("nextCursor").GetString();
        using var secondPage = await first.GetAsync(
            $"/api/v1/account/sessions?limit=2&cursor={Uri.EscapeDataString(cursor!)}",
            TestContext.Current.CancellationToken);
        using var secondDocument = JsonDocument.Parse(
            await secondPage.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var secondItems = secondDocument.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Equal(2, firstItems.Length);
        Assert.Single(secondItems);
        Assert.NotNull(cursor);
        Assert.Equal(
            [3, 2],
            firstItems.Select(item => item.GetProperty("lastSeenAt").GetDateTimeOffset().Minute));
        Assert.Equal(1, secondItems[0].GetProperty("lastSeenAt").GetDateTimeOffset().Minute);
        var allItems = firstItems.Concat(secondItems).ToArray();
        Assert.Single(
            allItems,
            item => item.GetProperty("isCurrent").GetBoolean());
        Assert.Contains(
            allItems,
            item => item.GetProperty("ipAddress").GetString() == "203.0.113.0/24");
        Assert.Contains(
            allItems,
            item => item.GetProperty("ipAddress").GetString() == "2001:db8:abcd:12::/64");
        Assert.DoesNotContain(
            "203.0.113.99",
            firstDocument.RootElement.ToString() + secondDocument.RootElement);
        Assert.DoesNotContain(
            "ticket",
            firstDocument.RootElement.ToString() + secondDocument.RootElement,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "hash",
            firstDocument.RootElement.ToString() + secondDocument.RootElement,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionPageUsesTwentyAsTheDefaultLimit()
    {
        using var client = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Default Page",
            "local-agent+default-session-page@local-agent.test");
        await AccountEndpointTestSupport.AddActiveSessionsAsync(
            factory.Services,
            scenario.UserId,
            count: 20);

        using var response = await client.GetAsync(
            "/api/v1/account/sessions",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var data = document.RootElement.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, data.GetProperty("items").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(
            data.GetProperty("nextCursor").GetString()));
    }

    [Theory]
    [InlineData("?limit=0", "validation_failed")]
    [InlineData("?limit=101", "validation_failed")]
    [InlineData("?limit=not-a-number", "invalid_request")]
    [InlineData("?cursor=not-an-opaque-cursor", "invalid_cursor")]
    public async Task SessionQueryValidatesLimitAndOpaqueCursor(
        string query,
        string expectedCode)
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Session Validation",
            "local-agent+session-validation@local-agent.test");

        using var response = await client.GetAsync(
            $"/api/v1/account/sessions{query}",
            TestContext.Current.CancellationToken);
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, problem.Code);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ForeignSessionUsesTheSameNotFoundProblemAsMissingSession()
    {
        using var owner = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            owner,
            "Session Owner",
            "local-agent+session-owner@local-agent.test");
        using var foreign = factory.CreateApiClient();
        var foreignScenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            foreign,
            "Foreign Session",
            "local-agent+foreign-session@local-agent.test");
        var foreignSessionId = await AccountEndpointTestSupport.GetOnlySessionIdAsync(
            factory.Services,
            foreignScenario.UserId);

        using var foreignResponse =
            await AccountEndpointTestSupport.SendWithCsrfAsync(
                owner,
                HttpMethod.Delete,
                $"/api/v1/account/sessions/{foreignSessionId}");
        using var missingResponse =
            await AccountEndpointTestSupport.SendWithCsrfAsync(
                owner,
                HttpMethod.Delete,
                $"/api/v1/account/sessions/{Guid.CreateVersion7()}");
        var foreignProblem =
            await AccountEndpointTestSupport.ReadProblemAsync(foreignResponse);
        var missingProblem =
            await AccountEndpointTestSupport.ReadProblemAsync(missingResponse);

        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal("account_session_not_found", foreignProblem.Code);
        Assert.Equal(missingProblem.Code, foreignProblem.Code);
    }

    [Fact]
    public async Task CurrentSessionCannotBeRevokedByTheSingleSessionRoute()
    {
        using var client = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Current Session",
            "local-agent+current-session@local-agent.test");
        var currentSessionId = await AccountEndpointTestSupport.GetOnlySessionIdAsync(
            factory.Services,
            scenario.UserId);

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/account/sessions/{currentSessionId}");
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("current_session_cannot_be_revoked", problem.Code);
    }

    [Fact]
    public async Task RevokeOthersDeletesEveryOtherTicketAndPreservesCurrentSession()
    {
        using var current = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            current,
            "Bulk Session",
            "local-agent+bulk-session@local-agent.test",
            "local-bulk-session-password");
        using var other = factory.CreateApiClient();
        using var signIn = await LocalAuthTestClient.SignInAsync(
            other,
            scenario.Email,
            scenario.Password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        await AccountEndpointTestSupport.AddExpiredSessionAsync(
            factory.Services,
            scenario.UserId);

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            current,
            HttpMethod.Delete,
            "/api/v1/account/sessions/others");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        using var account = await current.GetAsync(
            "/api/v1/account",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            2,
            document.RootElement
                .GetProperty("data")
                .GetProperty("revokedCount")
                .GetInt32());
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var remaining = await db.Sessions.Where(
                session => session.UserId == scenario.UserId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Single(remaining);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"confirmationEmail\":null}")]
    [InlineData("{\"confirmationEmail\":\"wrong@example.test\"}")]
    [InlineData("{\"confirmationEmail\":\"local-agent+delete-mismatch@local-agent.test\",\"extra\":true}")]
    public async Task AccountDeleteRequiresExactStrictConfirmation(string body)
    {
        using var client = factory.CreateApiClient();
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Delete Mismatch",
            "local-agent+delete-mismatch@local-agent.test");

        using var response = await AccountEndpointTestSupport.SendRawWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account",
            body,
            "application/json");
        var problem = await AccountEndpointTestSupport.ReadProblemAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            problem.Code,
            new[] { "invalid_request", "validation_failed" });
        if (problem.Code == "validation_failed")
        {
            Assert.Contains("confirmationEmail", problem.Errors!.Keys);
        }
    }

    [Fact]
    public async Task AccountDeleteCascadesOwnedDataAndExpiresTheBrowserCookie()
    {
        using var client = factory.CreateApiClient();
        var scenario = await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Delete Owner",
            "local-agent+delete-owner@local-agent.test");
        await AccountEndpointTestSupport.SeedExternalLoginAsync(
            factory.Services,
            scenario.UserId,
            "google",
            "delete-subject");

        using var response = await AccountEndpointTestSupport.SendWithCsrfAsync(
            client,
            HttpMethod.Delete,
            "/api/v1/account",
            new { confirmationEmail = "  LOCAL-AGENT+DELETE-OWNER@LOCAL-AGENT.TEST  " });
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        using var after = await client.GetAsync(
            "/api/v1/account",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            document.RootElement
                .GetProperty("data")
                .GetProperty("deleted")
                .GetBoolean());
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value =>
                value.StartsWith(
                    "__Host-template.session=",
                    StringComparison.Ordinal)
                && value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.False(await db.Users.AnyAsync(
            user => user.Id == scenario.UserId,
            TestContext.Current.CancellationToken));
        Assert.False(await db.UserEmails.AnyAsync(
            email => email.UserId == scenario.UserId,
            TestContext.Current.CancellationToken));
        Assert.False(await db.UserLogins.AnyAsync(
            login => login.UserId == scenario.UserId,
            TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(
            session => session.UserId == scenario.UserId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidStoredIpAddressesProjectAsNull()
    {
        await using var invalidIpFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAccountSessionStore>();
                services.AddScoped<IAccountSessionStore, InvalidIpSessionStore>();
            }));
        using var client = invalidIpFactory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });
        await AccountEndpointTestSupport.CreateScenarioAsync(
            client,
            "Invalid IP",
            "local-agent+invalid-ip@local-agent.test");

        using var response = await client.GetAsync(
            "/api/v1/account/sessions",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var item = Assert.Single(
            document.RootElement
                .GetProperty("data")
                .GetProperty("items")
                .EnumerateArray());

        Assert.Equal(JsonValueKind.Null, item.GetProperty("ipAddress").ValueKind);
    }

    private sealed class InvalidIpSessionStore : IAccountSessionStore
    {
        public Task<CursorPage<AccountSession>> ListAsync(
            UserId userId,
            SessionCursor? cursor,
            int limit,
            CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new CursorPage<AccountSession>(
                [
                    new AccountSession(
                        SessionId.New(),
                        now.AddHours(-1),
                        now,
                        now.AddDays(1),
                        "local",
                        "not-an-ip-address",
                        "bounded-agent")
                ],
                null));
        }

        public Task<bool> RevokeAsync(
            UserId userId,
            SessionId sessionId,
            CancellationToken ct) =>
            Task.FromResult(false);

        public Task<int> RevokeOthersAsync(
            UserId userId,
            SessionId current,
            CancellationToken ct) =>
            Task.FromResult(0);
    }

    public enum DisconnectTerminalState
    {
        Removed,
        Unsafe,
        StillSafe
    }

    private sealed class RacingAccountStore(
        bool profileMissing = false,
        DisconnectTerminalState terminalState =
            DisconnectTerminalState.StillSafe)
        : IAccountStore
    {
        public int DisconnectSnapshotReads { get; private set; }

        public int DisconnectAttempts { get; private set; }

        public Task<AccountSnapshot?> GetAsync(
            UserId userId,
            CancellationToken ct) =>
            Task.FromResult<AccountSnapshot?>(null);

        public Task<AccountSnapshot?> UpdateDisplayNameAsync(
            UserId userId,
            string displayName,
            CancellationToken ct) =>
            profileMissing
                ? Task.FromResult<AccountSnapshot?>(null)
                : throw new InvalidOperationException(
                    "This test store only models a missing profile.");

        public Task<IReadOnlyList<AccountConnection>> ListConnectionsAsync(
            UserId userId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AccountConnection>>([]);

        public Task<DisconnectSnapshot?> GetDisconnectSnapshotAsync(
            UserId userId,
            ExternalProvider provider,
            CancellationToken ct)
        {
            DisconnectSnapshotReads++;
            if (DisconnectSnapshotReads == 3 &&
                terminalState == DisconnectTerminalState.Removed)
            {
                return Task.FromResult<DisconnectSnapshot?>(null);
            }

            var connectionCount = DisconnectSnapshotReads switch
            {
                1 => 3,
                2 => 2,
                _ when terminalState == DisconnectTerminalState.Unsafe => 1,
                _ => 4
            };
            return Task.FromResult<DisconnectSnapshot?>(
                new DisconnectSnapshot(
                    userId,
                    provider,
                    VerifiedEmail.Create("github@example.test"),
                    EmailIsPrimary: false,
                    connectionCount));
        }

        public Task DisconnectAsync(
            DisconnectSnapshot snapshot,
            CancellationToken ct)
        {
            DisconnectAttempts++;
            throw new AccountConcurrencyException();
        }

        public Task DeleteAsync(UserId userId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}

internal static class AccountEndpointTestSupport
{
    private static readonly DateTimeOffset SessionBase =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
    private static int _scenarioClientAddress;

    internal static async Task<TestScenario> CreateScenarioAsync(
        HttpClient client,
        string name,
        string email,
        string password = "local-account-test-password")
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Headers.Add(
            "X-Forwarded-For",
            $"198.51.100.{Interlocked.Increment(ref _scenarioClientAddress) % 250 + 1}");
        request.Content = JsonContent.Create(new { name, email, password });
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<
            LocalAuthTestClient.ScenarioEnvelope>(
            TestContext.Current.CancellationToken);
        return new TestScenario(
            envelope!.Data.User.Id,
            envelope.Data.Email,
            envelope.Data.Password);
    }

    internal static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SendRawWithCsrfAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string body,
        string mediaType)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = new StringContent(body, Encoding.UTF8, mediaType);
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task SeedExternalLoginAsync(
        IServiceProvider services,
        Guid userId,
        string provider,
        string subject)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var emailId = await db.UserEmails
            .Where(email => email.UserId == userId && email.IsPrimary)
            .Select(email => email.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        db.UserLogins.Add(new ApplicationUserLogin
        {
            UserId = userId,
            LoginProvider = provider,
            ProviderKey = subject,
            ProviderDisplayName = provider,
            VerifiedEmailId = emailId,
            ConnectedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LastUsedAt = DateTimeOffset.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task<Guid> GetOnlySessionIdAsync(
        IServiceProvider services,
        Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions
            .Where(session => session.UserId == userId)
            .Select(session => session.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task SetSessionMetadataAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var sessions = await db.Sessions
            .OrderBy(session => session.CreatedAt)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, sessions.Length);
        var addresses = new[]
        {
            IPAddress.Parse("192.0.2.77"),
            IPAddress.Parse("2001:db8:abcd:12:1111:2222:3333:4444"),
            IPAddress.Parse("203.0.113.99")
        };
        for (var index = 0; index < sessions.Length; index++)
        {
            sessions[index].UpdatedAt = SessionBase.AddMinutes(index + 1);
            sessions[index].IpAddress = addresses[index];
            sessions[index].UserAgent = $"bounded-agent-{index + 1}";
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task AddExpiredSessionAsync(
        IServiceProvider services,
        Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var created = DateTimeOffset.UtcNow.AddDays(-10);
        db.Sessions.Add(new AuthSessionEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TicketKeyHash = Guid.NewGuid().ToByteArray(),
            ProtectedTicket = Guid.NewGuid().ToByteArray(),
            CreatedAt = created,
            UpdatedAt = created.AddHours(1),
            ExpiresAt = created.AddDays(1),
            AuthenticationMethod = "local"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task AddActiveSessionsAsync(
        IServiceProvider services,
        Guid userId,
        int count)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        for (var index = 0; index < count; index++)
        {
            var created = DateTimeOffset.UtcNow.AddMinutes(-index - 1);
            db.Sessions.Add(new AuthSessionEntity
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                TicketKeyHash = Guid.NewGuid().ToByteArray(),
                ProtectedTicket = Guid.NewGuid().ToByteArray(),
                CreatedAt = created,
                UpdatedAt = created,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                AuthenticationMethod = "local"
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task<ApiProblem> ReadProblemAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException("A Problem Details body is required.");

    internal static string[] PropertyNames(JsonElement value) =>
        value.EnumerateObject().Select(property => property.Name).ToArray();

    internal sealed record TestScenario(Guid UserId, string Email, string Password);

    internal sealed record ApiProblem(
        string Code,
        Dictionary<string, string[]>? Errors);
}
