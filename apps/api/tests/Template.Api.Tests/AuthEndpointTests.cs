using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Api.Authentication;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests;

public sealed class AuthEndpointTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CapabilitiesAndAnonymousSessionAreTypedAndNotCached()
    {
        using var client = factory.CreateApiClient();

        using var capabilities = await client.GetAsync(
            "/api/v1/auth/capabilities",
            TestContext.Current.CancellationToken);
        using var session = await client.GetAsync(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var capabilitiesBody = await capabilities.Content
            .ReadFromJsonAsync<CapabilitiesEnvelope>(
                TestContext.Current.CancellationToken);
        var sessionBody = await session.Content.ReadFromJsonAsync<SessionEnvelope>(
            TestContext.Current.CancellationToken);

        Assert.True(capabilitiesBody!.Data.LocalAutomationEnabled);
        Assert.Empty(capabilitiesBody.Data.Providers);
        Assert.False(sessionBody!.Data.Authenticated);
        Assert.Null(sessionBody.Data.User);
        Assert.Null(sessionBody.Data.Session);
        Assert.Contains("no-store", capabilities.Headers.CacheControl!.ToString());
        Assert.Contains("no-store", session.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ScenarioCreatesExactlyOnePersistentUserAndSession()
    {
        using var client = factory.CreateApiClient();

        using var response = await LocalAuthTestClient.CreateScenarioAsync(client);
        var payload = await response.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Matches(
            "^local-agent\\+.+@local-agent\\.test$",
            payload!.Data.Email);
        Assert.StartsWith("local-", payload.Data.Password);
        Assert.Equal("/api/local-auth/scenario", payload.Data.CleanupUrl);
        Assert.Equal(1, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Sessions.CountAsync(TestContext.Current.CancellationToken));
        var setCookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("__Host-template.session=", StringComparison.Ordinal));
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScenarioAcceptsAnEmptyOptionalRequestBody()
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(await response.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReloadAndSecondCredentialSignInHaveDistinctSessionIds()
    {
        using var first = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(first);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        var firstSession = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var reloaded = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        using var second = factory.CreateApiClient();
        using var signedIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario!.Data.Email,
            scenario.Data.Password);
        var secondSession = await second.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.True(firstSession!.Data.Authenticated);
        Assert.Equal(firstSession.Data.Session!.Id, reloaded!.Data.Session!.Id);
        Assert.NotEqual(firstSession.Data.Session.Id, secondSession!.Data.Session!.Id);
    }

    [Fact]
    public async Task LogoutDeletesOnlyCurrentSession()
    {
        using var first = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(first);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        using var second = factory.CreateApiClient();
        using var signedIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario!.Data.Email,
            scenario.Data.Password);

        using var logout = await LocalAuthTestClient.LogoutAsync(first);
        var firstState = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var secondState = await second.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var sessionCount = await scope.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .Sessions.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.False(firstState!.Data.Authenticated);
        Assert.True(secondState!.Data.Authenticated);
        Assert.Equal(1, sessionCount);
        var expiredCookie = logout.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal));
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            expiredCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CleanupDeletesUserAndEverySession()
    {
        using var first = factory.CreateApiClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(first);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        using var second = factory.CreateApiClient();
        using var signedIn = await LocalAuthTestClient.SignInAsync(
            second,
            scenario!.Data.Email,
            scenario.Data.Password);

        using var cleanup = await LocalAuthTestClient.CleanupAsync(second);
        var firstState = await first.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
        Assert.False(firstState!.Data.Authenticated);
        Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnknownMemberIsInvalidAndExplicitDuplicateIsConflict()
    {
        using var client = factory.CreateApiClient();
        using var unknown = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new { unsupported = true });
        var explicitBody = new
        {
            name = "Explicit User",
            email = "local-agent+explicit@local-agent.test",
            password = "local-explicit-password"
        };
        using var first = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            explicitBody);
        using var duplicateClient = factory.CreateApiClient();
        using var duplicate = await LocalAuthTestClient.CreateScenarioAsync(
            duplicateClient,
            explicitBody);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(
            "invalid_request",
            (await unknown.Content.ReadFromJsonAsync<ApiProblem>(
                TestContext.Current.CancellationToken))!.Code);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "local_auth_user_exists",
            (await duplicate.Content.ReadFromJsonAsync<ApiProblem>(
                TestContext.Current.CancellationToken))!.Code);
    }

    [Fact]
    public async Task ExplicitNameAndEmailAreTrimmedAndEmailIsNormalized()
    {
        using var client = factory.CreateApiClient();

        using var response = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "  Trimmed User  ",
                email = "  LOCAL-AGENT+TRIMMED@LOCAL-AGENT.TEST  ",
                password = "local-trimmed-password"
            });
        var scenario = await response.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Trimmed User", scenario!.Data.User.Name);
        Assert.Equal(
            "local-agent+trimmed@local-agent.test",
            scenario.Data.Email);
    }

    [Fact]
    public async Task IdentityRejectedLocalEmailUsesStableValidationWithoutCreatingState()
    {
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "100"
                    })));
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var response = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Identity Rejected",
                email = "local-agent+foo!@local-agent.test",
                password = "local-identity-rejected-password"
            });
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        await using var scope = isolated.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", problem!.Code);
        Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("__Host-template.session", response.Headers.ToString());
        Assert.DoesNotContain("InvalidUserName", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "AllowedUserNameCharacters",
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Identity", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownIdentityResultRemainsInternalWithoutCreatingState()
    {
        await using var isolated = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<
                    IUserValidator<ApplicationUser>,
                    UnknownIdentityResultValidator>()));
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var response = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Unknown Identity Result",
                email = "local-agent+unknown-result@local-agent.test",
                password = "local-unknown-result-password"
            });
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal_error");
        await using var scope = isolated.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("__Host-template.session", response.Headers.ToString());
        Assert.DoesNotContain(
            UnknownIdentityResultValidator.ErrorCode,
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            UnknownIdentityResultValidator.ErrorDescription,
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MixedDuplicateAndUnknownIdentityResultsRemainInternalWithoutCreatingState()
    {
        const string email = "local-agent+mixed-identity-result@local-agent.test";
        var scenario = new
        {
            name = "Mixed Identity Result",
            email,
            password = "local-mixed-identity-result-password"
        };
        await using var seedHost = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "100"
                    })));
        using var seedClient = seedHost.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
        using var seeded = await LocalAuthTestClient.CreateScenarioAsync(
            seedClient,
            scenario);
        Assert.Equal(HttpStatusCode.Created, seeded.StatusCode);
        await using (var seedScope = seedHost.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await seedDb.Sessions.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        await using var isolated = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "100"
                    }));
            builder.ConfigureTestServices(services =>
                services.AddScoped<
                    IUserValidator<ApplicationUser>,
                    UnknownIdentityResultValidator>());
        });
        using var client = isolated.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var response = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            scenario);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal_error");
        await using var scope = isolated.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        Assert.Equal(1, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("__Host-template.session", response.Headers.ToString());
        Assert.DoesNotContain(
            UnknownIdentityResultValidator.ErrorCode,
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            UnknownIdentityResultValidator.ErrorDescription,
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScenarioAcceptsPaddingAroundMaximumTrimmedInputs()
    {
        using var client = factory.CreateApiClient();
        var name = new string('N', 50);
        var email =
            $"LOCAL-AGENT+{new string('A', 225)}@LOCAL-AGENT.TEST";

        using var response = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = $"  {name}  ",
                email = $"  {email}  ",
                password = "local-padded-maximum-password"
            });
        var scenario = await response.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(name, scenario!.Data.User.Name);
        Assert.Equal(email.ToLowerInvariant(), scenario.Data.Email);
    }

    [Fact]
    public async Task InvalidCredentialFailureDoesNotRevealUserExistence()
    {
        using var client = factory.CreateApiClient();
        using var missing = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+missing@local-agent.test",
            "local-invalid-password");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        var problem = await missing.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal("local_auth_invalid_credentials", problem!.Code);
        Assert.DoesNotContain("missing", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledFlagHidesLocalRoutesAndCapabilities()
    {
        await using var disabled = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:Enabled"] = "false"
                    })));
        using var client = disabled.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        var capabilities = await client.GetFromJsonAsync<CapabilitiesEnvelope>(
            "/api/v1/auth/capabilities",
            TestContext.Current.CancellationToken);
        using var hidden = await client.PostAsync(
            "/api/local-auth/scenario",
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken);

        Assert.False(capabilities!.Data.LocalAutomationEnabled);
        Assert.Empty(capabilities.Data.Providers);
        await AssertProblemAsync(
            hidden,
            HttpStatusCode.NotFound,
            "local_auth_disabled");
    }

    [Fact]
    public async Task EveryUnsafeAuthEndpointRejectsMissingAntiforgery()
    {
        using var client = factory.CreateApiClient();

        using var scenarioWithoutToken = await client.PostAsync(
            "/api/local-auth/scenario",
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            scenarioWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var invalidTokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        invalidTokenRequest.Headers.Add("X-CSRF-TOKEN", "invalid");
        invalidTokenRequest.Content = JsonContent.Create(new { });
        using var scenarioWithInvalidToken = await client.SendAsync(
            invalidTokenRequest,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            scenarioWithInvalidToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var created = await LocalAuthTestClient.CreateScenarioAsync(client);
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var signInWithoutToken = await client.PostAsync(
            "/api/local-auth/sign-in",
            JsonContent.Create(new
            {
                scenario!.Data.Email,
                scenario.Data.Password
            }),
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            signInWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var logoutWithoutToken = await client.PostAsync(
            "/api/v1/auth/logout",
            content: null,
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            logoutWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var cleanupWithoutToken = await client.DeleteAsync(
            "/api/local-auth/scenario",
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            cleanupWithoutToken,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
    }

    [Theory]
    [InlineData("POST", "/api/v1/auth/logout")]
    [InlineData("DELETE", "/api/local-auth/scenario")]
    public async Task AnonymousProtectedMutationUsesUnauthorized(
        string method,
        string path)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "unauthorized");
    }

    [Fact]
    public async Task MalformedJsonAndInvalidFieldsUseDistinctStableProblems()
    {
        using var client = factory.CreateApiClient();
        var csrf = await LocalAuthTestClient.GetCsrfAsync(client);
        using var malformedRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        malformedRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        malformedRequest.Content = new StringContent(
            "{",
            Encoding.UTF8,
            "application/json");
        using var malformed = await client.SendAsync(
            malformedRequest,
            TestContext.Current.CancellationToken);

        using var shortName = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new { name = "x" });
        using var shortPassword = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+validation@local-agent.test",
            "short");

        await AssertProblemAsync(
            malformed,
            HttpStatusCode.BadRequest,
            "invalid_request");
        await AssertProblemAsync(
            shortName,
            HttpStatusCode.BadRequest,
            "validation_failed");
        await AssertProblemAsync(
            shortPassword,
            HttpStatusCode.BadRequest,
            "validation_failed");
    }

    [Theory]
    [InlineData("/api/local-auth/scenario")]
    [InlineData("/api/local-auth/sign-in")]
    public async Task NonJsonAuthRequestBodiesUseStableInvalidRequest(string path)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = new StringContent("{}", Encoding.UTF8, "text/plain");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
    }

    [Theory]
    [InlineData("/api/local-auth/scenario", "text/plain")]
    [InlineData("/api/local-auth/scenario", "application/json")]
    [InlineData("/api/local-auth/sign-in", "text/plain")]
    [InlineData("/api/local-auth/sign-in", "application/json")]
    public async Task WhitespaceOnlyAuthRequestBodiesUseStableInvalidRequest(
        string path,
        string mediaType)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = new StringContent(" ", Encoding.UTF8, mediaType);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
    }

    [Theory]
    [InlineData(
        "/api/local-auth/scenario",
        """{"name":"invalid""",
        """-name"}""")]
    [InlineData(
        "/api/local-auth/sign-in",
        """{"email":"local-agent+missing@local-agent.test","password":"local-invalid-""",
        """-password"}""")]
    public async Task InvalidUtf8AuthJsonUsesStableInvalidRequest(
        string path,
        string prefix,
        string suffix)
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        request.Content = new ByteArrayContent(
        [
            .. Encoding.UTF8.GetBytes(prefix),
            0xff,
            .. Encoding.UTF8.GetBytes(suffix)
        ]);
        request.Content.Headers.ContentType = new("application/json");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_request");
    }

    [Fact]
    public async Task SignInLimiterReturnsTyped429AfterConfiguredPermit()
    {
        await using var limited = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["LocalAutomationAuth:SignInRateLimitPerFiveMinutes"] = "1"
                    })));
        using var client = limited.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var first = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+missing@local-agent.test",
            "local-invalid-password");
        using var second = await LocalAuthTestClient.SignInAsync(
            client,
            "local-agent+missing@local-agent.test",
            "local-invalid-password");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.True(second.Headers.RetryAfter is not null);
        await AssertProblemAsync(
            second,
            HttpStatusCode.TooManyRequests,
            "rate_limited");
    }

    [Fact]
    public async Task CleanupRejectsNonLocalSession()
    {
        using var client = factory.CreateApiClient();
        using var signIn = await client.PostAsync(
            "/api/testing/non-local-session",
            content: null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        using var cleanup = await LocalAuthTestClient.CleanupAsync(client);

        await AssertProblemAsync(
            cleanup,
            HttpStatusCode.Forbidden,
            "local_auth_user_required");
    }

    [Fact]
    public async Task TicketStoreFailureRollsBackScenarioUser()
    {
        await using var failing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<
                    IPostConfigureOptions<CookieAuthenticationOptions>>(
                    new FailingTicketStorePostConfigure())));
        using var client = failing.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });

        using var response = await LocalAuthTestClient.CreateScenarioAsync(client);
        await AssertProblemAsync(
            response,
            HttpStatusCode.InternalServerError,
            "internal_error");
        Assert.False(
            response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal)));
        await using var scope = failing.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.False(await db.Users.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.Sessions.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SecurityEventIsStructuredAndExcludesCredentialsAndCookie()
    {
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var client = factory.CreateApiClient();
        var email = $"local-agent+log-{Guid.NewGuid():N}@local-agent.test";
        const string password = "local-log-password";

        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Log Test",
                email,
                password
            });
        var scenario = await created.Content
            .ReadFromJsonAsync<LocalAuthTestClient.ScenarioEnvelope>(
                TestContext.Current.CancellationToken);
        var session = await client.GetFromJsonAsync<SessionEnvelope>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var securityEvent = Assert.Single(
            logs.Logs,
            log =>
                Equals(log.State.GetValueOrDefault("AuthOperation"), "scenario_create") &&
                Equals(log.State.GetValueOrDefault("AuthOutcome"), "succeeded"));
        var cookie = created.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal))
            .Split(';', 2)[0]
            .Split('=', 2)[1];
        var renderedLogs = string.Join(
            "\n",
            logs.Logs.Select(log =>
                $"{log.Message} {string.Join(" ", log.State.Values)}"));

        Assert.Equal(scenario!.Data.User.Id, securityEvent.State["UserId"]);
        Assert.Equal(session!.Data.Session!.Id, securityEvent.State["SessionId"]);
        Assert.DoesNotContain(email, renderedLogs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, renderedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, renderedLogs, StringComparison.Ordinal);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedCode, problem!.Code);
    }

    private sealed record CapabilitiesEnvelope(CapabilitiesData Data);
    private sealed record CapabilitiesData(
        bool LocalAutomationEnabled,
        ProviderData[] Providers);
    private sealed record ProviderData(string Id, string DisplayName);
    internal sealed record SessionEnvelope(SessionData Data);
    internal sealed record SessionData(
        bool Authenticated,
        LocalAuthTestClient.UserData? User,
        SessionMetadata? Session);
    internal sealed record SessionMetadata(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset ExpiresAt);
    private sealed record ApiProblem(string Code, string Detail);

    private sealed class UnknownIdentityResultValidator
        : IUserValidator<ApplicationUser>
    {
        internal const string ErrorCode = "CustomProviderFailure";
        internal const string ErrorDescription =
            "Sensitive custom provider failure detail.";

        public Task<IdentityResult> ValidateAsync(
            UserManager<ApplicationUser> manager,
            ApplicationUser user) =>
            Task.FromResult(
                IdentityResult.Failed(
                    new IdentityError
                    {
                        Code = ErrorCode,
                        Description = ErrorDescription
                    }));
    }

    private sealed class FailingTicketStore : ITicketStore
    {
        public Task<string> StoreAsync(AuthenticationTicket ticket) =>
            throw new IOException("Injected ticket storage failure.");

        public Task<string> StoreAsync(
            AuthenticationTicket ticket,
            CancellationToken cancellationToken) =>
            throw new IOException("Injected ticket storage failure.");

        public Task<string> StoreAsync(
            AuthenticationTicket ticket,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            throw new IOException("Injected ticket storage failure.");

        public Task RenewAsync(string key, AuthenticationTicket ticket) =>
            Task.CompletedTask;

        public Task RenewAsync(
            string key,
            AuthenticationTicket ticket,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RenewAsync(
            string key,
            AuthenticationTicket ticket,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
            Task.FromResult<AuthenticationTicket?>(null);

        public Task<AuthenticationTicket?> RetrieveAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticationTicket?>(null);

        public Task<AuthenticationTicket?> RetrieveAsync(
            string key,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticationTicket?>(null);

        public Task RemoveAsync(string key) => Task.CompletedTask;

        public Task RemoveAsync(
            string key,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string key,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FailingTicketStorePostConfigure
        : IPostConfigureOptions<CookieAuthenticationOptions>
    {
        public void PostConfigure(
            string? name,
            CookieAuthenticationOptions options)
        {
            if (name == ApiAuthenticationDefaults.IssuerSchemeName)
            {
                options.SessionStore = new FailingTicketStore();
            }
        }
    }
}
