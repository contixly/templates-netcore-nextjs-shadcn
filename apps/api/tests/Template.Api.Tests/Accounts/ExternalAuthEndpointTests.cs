using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Api.Tests.Accounts;

public sealed class ExternalAuthEndpointTests(
    PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private ExternalOAuthWebApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new ExternalOAuthWebApplicationFactory(postgres);
        await _factory.InitializeAsync();
    }

    public async ValueTask DisposeAsync() =>
        await _factory.DisposeAsync();

    [Fact]
    public async Task CapabilitiesExposeOnlyConfiguredProvidersWithoutCaching()
    {
        using var client = _factory.CreateOAuthClient();

        using var response = await client.GetAsync(
            "/api/v1/auth/capabilities",
            TestContext.Current.CancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<
            Envelope<Capabilities>>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(
            [
                new Provider("github", "GitHub"),
                new Provider("yandex", "Yandex")
            ],
            envelope!.Data.Providers);
    }

    [Fact]
    public async Task BothChallengeIntentsRequireAntiforgery()
    {
        using var anonymous = _factory.CreateOAuthClient();
        using var signIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/external/yandex/challenge",
            new { intent = "signIn" },
            TestContext.Current.CancellationToken);
        await AssertProblemAsync(
            signIn,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");

        using var authenticated = _factory.CreateOAuthClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            authenticated,
            new
            {
                name = "Connect Owner",
                email = "local-agent+csrf-connect@local-agent.test",
                password = "local-connect-password"
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var connect = await authenticated.PostAsJsonAsync(
            "/api/v1/auth/external/yandex/challenge",
            new { intent = "connect" },
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            connect,
            HttpStatusCode.BadRequest,
            "antiforgery_failed");
    }

    [Fact]
    public async Task SignInRequiresAnonymousAndConnectRequiresCurrentSession()
    {
        using var authenticated = _factory.CreateOAuthClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            authenticated);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var signIn = await ChallengeAsync(
            authenticated,
            "yandex",
            "signIn");
        await AssertProblemAsync(
            signIn,
            HttpStatusCode.Conflict,
            "already_authenticated");

        using var anonymous = _factory.CreateOAuthClient();
        using var connect = await ChallengeAsync(
            anonymous,
            "yandex",
            "connect");
        await AssertProblemAsync(
            connect,
            HttpStatusCode.Unauthorized,
            "unauthorized");
    }

    [Theory]
    [InlineData("")]
    [InlineData("dashboard")]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example/steal")]
    [InlineData("/%2f%2fevil.example/steal")]
    [InlineData("/%252f%252fevil.example/steal")]
    [InlineData("/%255c%255cevil.example/steal")]
    [InlineData("/safe%0apath")]
    [InlineData("/safe/../api/v1/auth/session")]
    [InlineData("/safe/..//evil.example")]
    [InlineData("/%2e%2e//evil.example")]
    [InlineData("/safe/%2e%2e/auth/login")]
    [InlineData("/%61pi/v1/auth/session")]
    [InlineData("/%61uth/login")]
    [InlineData("/api/v1/system/status")]
    [InlineData("/auth/login")]
    public async Task UnsafeReturnUrlsAreRejected(string returnUrl)
    {
        using var client = _factory.CreateOAuthClient();

        using var response = await ChallengeAsync(
            client,
            "yandex",
            "signIn",
            returnUrl);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "invalid_return_url");
    }

    [Theory]
    [InlineData("/search?next=%2Fdashboard", "safe-query")]
    [InlineData("/search#next=%2Fdashboard", "safe-fragment")]
    public async Task EncodedQueryAndFragmentReturnValuesSurviveTheCallback(
        string returnUrl,
        string code)
    {
        using var client = _factory.CreateOAuthClient();
        var callback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            code,
            returnUrl: returnUrl);

        using var response = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(returnUrl, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ChallengeReturnsOneExpectedHttpsAuthorizationUrlAsJson()
    {
        using var client = _factory.CreateOAuthClient();

        using var response = await ChallengeAsync(
            client,
            "yandex",
            "signIn",
            returnUrl: null);
        var envelope = await response.Content.ReadFromJsonAsync<
            Envelope<Authorization>>(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Null(response.Headers.Location);
        var authorizationUrl = new Uri(
            envelope!.Data.AuthorizationUrl,
            UriKind.Absolute);
        Assert.Equal(Uri.UriSchemeHttps, authorizationUrl.Scheme);
        Assert.Equal("oauth.yandex.ru", authorizationUrl.Host);
        Assert.Equal("/authorize", authorizationUrl.AbsolutePath);
        Assert.Single(QueryHelpers.ParseQuery(authorizationUrl.Query)["state"]);
    }

    [Fact]
    public async Task GoogleSelectsAnAccountOnlyForProductionChallenges()
    {
        using var certificate = TestDataProtectionCertificate.CreateRsa();
        await using var production = new ExternalOAuthWebApplicationFactory(
            postgres,
            Environments.Production,
            configureGoogle: true,
            certificate);
        await production.InitializeAsync();
        await using var nonProduction =
            new ExternalOAuthWebApplicationFactory(
                postgres,
                "Test",
                configureGoogle: true);
        await nonProduction.InitializeAsync();
        using var productionClient = production.CreateOAuthClient();
        using var nonProductionClient = nonProduction.CreateOAuthClient();

        using var productionResponse = await ChallengeAsync(
            productionClient,
            "google",
            "signIn");
        using var nonProductionResponse = await ChallengeAsync(
            nonProductionClient,
            "google",
            "signIn");
        var productionBody = await productionResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var nonProductionBody = await nonProductionResponse.Content
            .ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(
            productionResponse.StatusCode == HttpStatusCode.OK,
            $"Production challenge returned " +
            $"{(int)productionResponse.StatusCode}: {productionBody}" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                production.Services
                    .GetRequiredService<CapturedLogProvider>()
                    .Logs
                    .Select(log => string.Join(
                        " | ",
                        [
                            log.Message,
                            .. log.State.Select(pair =>
                                $"{pair.Key}={pair.Value}"),
                            log.Exception?.ToString()
                        ]))));
        Assert.True(
            nonProductionResponse.StatusCode == HttpStatusCode.OK,
            $"Non-Production challenge returned " +
            $"{(int)nonProductionResponse.StatusCode}: {nonProductionBody}");
        var productionEnvelope =
            System.Text.Json.JsonSerializer.Deserialize<
                Envelope<Authorization>>(
                    productionBody,
                    new System.Text.Json.JsonSerializerOptions(
                        System.Text.Json.JsonSerializerDefaults.Web));
        var nonProductionEnvelope =
            System.Text.Json.JsonSerializer.Deserialize<
                Envelope<Authorization>>(
                    nonProductionBody,
                    new System.Text.Json.JsonSerializerOptions(
                        System.Text.Json.JsonSerializerDefaults.Web));
        var productionQuery = QueryHelpers.ParseQuery(
            new Uri(
                productionEnvelope!.Data.AuthorizationUrl,
                UriKind.Absolute).Query);
        var nonProductionQuery = QueryHelpers.ParseQuery(
            new Uri(
                nonProductionEnvelope!.Data.AuthorizationUrl,
                UriKind.Absolute).Query);

        Assert.Equal("select_account", Assert.Single(productionQuery["prompt"]));
        Assert.False(nonProductionQuery.ContainsKey("prompt"));
    }

    [Theory]
    [InlineData("/api/auth/callback/google")]
    [InlineData("/api/auth/callback/github")]
    [InlineData("/api/auth/callback/gitlab")]
    [InlineData("/api/auth/callback/vk")]
    [InlineData("/api/auth/oauth2/callback/yandex")]
    public async Task CallbackPathsAllowExactlyGetAndPost(string path)
    {
        using var client = _factory.CreateOAuthClient();
        using var get = await client.GetAsync(
            path,
            TestContext.Current.CancellationToken);
        using var post = await client.PostAsync(
            path,
            new FormUrlEncodedContent([]),
            TestContext.Current.CancellationToken);
        using var put = await client.PutAsync(
            path,
            JsonContent.Create(new { }),
            TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
    }

    [Fact]
    public async Task CallbackStateIsRedeemedOnlyOnce()
    {
        using var client = _factory.CreateOAuthClient();
        var callback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            "replay-success");

        using var first = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        using var replay = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);

        Assert.True(
            first.StatusCode == HttpStatusCode.Redirect,
            $"First callback returned {(int)first.StatusCode}: " +
            await first.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken) +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                _factory.OAuth.Requests.Select(request =>
                    $"{request.Method} {request.Uri}")));
        Assert.Equal("/dashboard", first.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, replay.StatusCode);
        Assert.Equal(
            "/auth/error?code=external_auth_failed",
            replay.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task MissingAndUnverifiedEmailsUseStableSafeRedirects()
    {
        using var missingClient = _factory.CreateOAuthClient();
        var missingCallback = await CompleteAuthorizationAsync(
            missingClient,
            "yandex",
            "missing-email");
        using var missing = await missingClient.GetAsync(
            missingCallback,
            TestContext.Current.CancellationToken);

        using var unverifiedClient = _factory.CreateOAuthClient();
        var unverifiedCallback = await CompleteAuthorizationAsync(
            unverifiedClient,
            "github",
            "unverified-email");
        using var unverified = await unverifiedClient.GetAsync(
            unverifiedCallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "/auth/error?code=external_email_required",
            missing.Headers.Location!.OriginalString);
        Assert.Equal(
            "/auth/error?code=external_email_unverified",
            unverified.Headers.Location!.OriginalString);
        Assert.DoesNotContain(
            "__Host-template.session",
            missing.Headers.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__Host-template.session",
            unverified.Headers.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingProviderAndEmailOwnershipConflictsAreSafe()
    {
        using var owner = _factory.CreateOAuthClient();
        var ownerCallback = await CompleteAuthorizationAsync(
            owner,
            "yandex",
            "existing-subject-owner");
        using var ownerResult = await owner.GetAsync(
            ownerCallback,
            TestContext.Current.CancellationToken);
        Assert.Equal("/dashboard", ownerResult.Headers.Location!.OriginalString);

        using var other = _factory.CreateOAuthClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            other,
            new
            {
                name = "Other Owner",
                email = "local-agent+other-owner@local-agent.test",
                password = "local-other-owner-password"
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var anonymous = _factory.CreateOAuthClient();
        var conflictCallback = await CompleteAuthorizationAsync(
            anonymous,
            "yandex",
            "existing-subject-other-email");
        using var conflict = await anonymous.GetAsync(
            conflictCallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "/auth/error?code=external_email_conflict",
            conflict.Headers.Location!.OriginalString);
        Assert.DoesNotContain(
            "local-agent+other-owner",
            conflict.Headers.Location.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectRejectsAProviderIdentityOwnedByAnotherUser()
    {
        using var owner = _factory.CreateOAuthClient();
        var ownerCallback = await CompleteAuthorizationAsync(
            owner,
            "yandex",
            "existing-subject-owner");
        using var ownerResult = await owner.GetAsync(
            ownerCallback,
            TestContext.Current.CancellationToken);
        Assert.Equal("/dashboard", ownerResult.Headers.Location!.OriginalString);

        using var other = _factory.CreateOAuthClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            other,
            new
            {
                name = "Connect Other Owner",
                email = "local-agent+connect-other@local-agent.test",
                password = "local-connect-other-password"
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var callback = await CompleteAuthorizationAsync(
            other,
            "yandex",
            "existing-subject-owner",
            "connect");

        using var conflict = await other.GetAsync(
            callback,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "/auth/error?code=external_identity_conflict",
            conflict.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ConnectRevalidatesInitiatingUserAndSession()
    {
        const string email =
            "local-agent+changed-context@local-agent.test";
        const string password = "local-changed-context-password";
        using var client = _factory.CreateOAuthClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Changed Context",
                email,
                password
            });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var callback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            "connect-success",
            "connect");

        using var logoutRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/logout");
        logoutRequest.Headers.Add(
            "X-CSRF-TOKEN",
            await LocalAuthTestClient.GetCsrfAsync(client));
        using var logout = await client.SendAsync(
            logoutRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var signIn = await LocalAuthTestClient.SignInAsync(
            client,
            email,
            password);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        using var response = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "/auth/error?code=oauth_flow_context_changed",
            response.Headers.Location!.OriginalString);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.False(await db.UserLogins.AnyAsync(
            login => login.LoginProvider == "yandex",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuccessfulSignInIssuesProviderSessionWithoutPersistingTokens()
    {
        using var client = _factory.CreateOAuthClient();
        var callback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            "sign-in-success",
            returnUrl: "/welcome?from=oauth");

        using var response = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        var session = await client.GetFromJsonAsync<Envelope<SessionProjection>>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/welcome?from=oauth",
            response.Headers.Location!.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal));
        Assert.True(session!.Data.Authenticated);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var stored = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal("yandex", stored.AuthenticationMethod);
        Assert.All(
            await db.OpenIddictTokens.AsNoTracking().ToArrayAsync(
                TestContext.Current.CancellationToken),
            token =>
            {
                Assert.Equal(
                    TokenTypeIdentifiers.Private.StateToken,
                    token.Type);
                Assert.DoesNotContain(
                    "ephemeral-yandex-token",
                    token.Payload ?? string.Empty,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task SuccessfulConnectRenewsSameSessionAndPreservesItsMethod()
    {
        using var client = _factory.CreateOAuthClient();
        using var created = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Local Connect Owner",
                email = "local-agent+connect-owner@local-agent.test",
                password = "local-connect-owner-password"
            });
        var before = await client.GetFromJsonAsync<Envelope<SessionProjection>>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);
        var callback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            "connect-success",
            "connect",
            "/user/connections?connected=yandex");

        using var response = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        var after = await client.GetFromJsonAsync<Envelope<SessionProjection>>(
            "/api/v1/auth/session",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "/user/connections?connected=yandex",
            response.Headers.Location!.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "__Host-template.session=",
                StringComparison.Ordinal));
        Assert.Equal(before!.Data.Session!.Id, after!.Data.Session!.Id);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var stored = await db.Sessions.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal("local", stored.AuthenticationMethod);
        Assert.Equal(
            "connect-success-subject",
            (await db.UserLogins.SingleAsync(
                TestContext.Current.CancellationToken)).ProviderKey);
    }

    [Fact]
    public async Task ProviderErrorsNeverExposeRawProviderText()
    {
        const string rawError = "private-provider-description-442";
        using var client = _factory.CreateOAuthClient();
        var authorization = await StartAuthorizationAsync(
            client,
            "yandex",
            "signIn",
            null);
        var state = QueryHelpers.ParseQuery(authorization.Query)["state"]
            .ToString();
        var callback =
            $"/api/auth/oauth2/callback/yandex?error=access_denied" +
            $"&error_description={Uri.EscapeDataString(rawError)}" +
            $"&state={Uri.EscapeDataString(state)}";

        using var response = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "/auth/error?code=external_auth_failed",
            response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(rawError, response.Headers.ToString());
        Assert.DoesNotContain(rawError, body);
        Assert.DoesNotContain(rawError, CapturedLogs());
    }

    [Fact]
    public async Task OAuthAuditLogsContainOnlyBoundedSafeContext()
    {
        using var client = _factory.CreateOAuthClient();
        var authorization = await StartAuthorizationAsync(
            client,
            "yandex",
            "signIn",
            null);
        var state = QueryHelpers.ParseQuery(authorization.Query)["state"]
            .ToString();
        const string code = "audit-code";
        var callback =
            $"/api/auth/oauth2/callback/yandex?code={code}" +
            $"&state={Uri.EscapeDataString(state)}";

        using var response = await client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        var logs = CapturedLogs();

        Assert.Contains("yandex", logs, StringComparison.Ordinal);
        Assert.Contains("succeeded", logs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CorrelationId", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("audit-owner@example.test", logs);
        Assert.DoesNotContain("audit-subject-991", logs);
        Assert.DoesNotContain(state, logs);
        Assert.DoesNotContain(code, logs);
        Assert.DoesNotContain(
            "ephemeral-yandex-token-audit-code",
            logs);
        Assert.DoesNotContain("__Host-template.session=", logs);
    }

    [Fact]
    public async Task ChallengeUsesFixedWindowRateLimit()
    {
        await using var limited = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ExternalOAuthSecurity:ChallengePermitLimitPerMinute"] =
                            "1"
                    })));
        using var client = limited.CreateClient(
            ExternalOAuthWebApplicationFactory.OAuthClientOptions);

        using var first = await ChallengeAsync(
            client,
            "yandex",
            "signIn");
        using var second = await ChallengeAsync(
            client,
            "yandex",
            "signIn");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertProblemAsync(
            second,
            HttpStatusCode.TooManyRequests,
            "rate_limited");
    }

    [Fact]
    public async Task CallbackRateLimitRunsBeforeProviderExchange()
    {
        await using var limited = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ExternalOAuthSecurity:CallbackPermitLimitPerFiveMinutes"] =
                            "1"
                    })));
        using var client = limited.CreateClient(
            ExternalOAuthWebApplicationFactory.OAuthClientOptions);
        var firstCallback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            "callback-rate-first");
        var secondCallback = await CompleteAuthorizationAsync(
            client,
            "yandex",
            "callback-rate-second");

        using var first = await client.GetAsync(
            firstCallback,
            TestContext.Current.CancellationToken);
        var exchangedRequestCount = _factory.OAuth.Requests.Count;
        using var second = await client.GetAsync(
            secondCallback,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        await AssertProblemAsync(
            second,
            HttpStatusCode.TooManyRequests,
            "rate_limited");
        Assert.Equal(
            exchangedRequestCount,
            _factory.OAuth.Requests.Count);
    }

    [Fact]
    public async Task OAuthRateLimitsMustAllBePositive()
    {
        await using var invalid = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ExternalOAuthSecurity:CallbackConcurrencyLimit"] = "0"
                    })));

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = invalid.CreateClient(
                ExternalOAuthWebApplicationFactory.OAuthClientOptions);
            using var response = await client.GetAsync(
                "/api/v1/auth/capabilities",
                TestContext.Current.CancellationToken);
        });

        Assert.Contains(
            "positive",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Uri> CompleteAuthorizationAsync(
        HttpClient client,
        string provider,
        string code,
        string intent = "signIn",
        string? returnUrl = null)
    {
        var authorization = await StartAuthorizationAsync(
            client,
            provider,
            intent,
            returnUrl);
        var state = QueryHelpers.ParseQuery(authorization.Query)["state"]
            .ToString();
        Assert.False(string.IsNullOrWhiteSpace(state));
        return new Uri(
            $"{CallbackPath(provider)}?code={Uri.EscapeDataString(code)}" +
            $"&state={Uri.EscapeDataString(state)}",
            UriKind.Relative);
    }

    private async Task<Uri> StartAuthorizationAsync(
        HttpClient client,
        string provider,
        string intent,
        string? returnUrl)
    {
        using var response = await ChallengeAsync(
            client,
            provider,
            intent,
            returnUrl);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Challenge returned {(int)response.StatusCode}: {body}" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                _factory.Services
                    .GetRequiredService<CapturedLogProvider>()
                    .Logs
                    .Select(log => string.Join(
                        " | ",
                        [
                            log.Message,
                            .. log.State.Select(pair =>
                                $"{pair.Key}={pair.Value}"),
                            log.Exception?.ToString()
                        ]))));
        var envelope = await response.Content.ReadFromJsonAsync<
            Envelope<Authorization>>(
            TestContext.Current.CancellationToken);
        return new Uri(envelope!.Data.AuthorizationUrl, UriKind.Absolute);
    }

    private static async Task<HttpResponseMessage> ChallengeAsync(
        HttpClient client,
        string provider,
        string intent,
        string? returnUrl = null)
    {
        var csrf = await LocalAuthTestClient.GetCsrfAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/auth/external/{provider}/challenge");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Content = JsonContent.Create(new { intent, returnUrl });
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static string CallbackPath(string provider) =>
        provider == "yandex"
            ? "/api/auth/oauth2/callback/yandex"
            : $"/api/auth/callback/{provider}";

    private string CapturedLogs() =>
        string.Join(
            '\n',
            _factory.Services
                .GetRequiredService<CapturedLogProvider>()
                .Logs
                .Select(log => string.Join(
                    " | ",
                    [
                        log.Message,
                        .. log.State.Values.Select(value => value?.ToString()),
                        .. log.Scope.Values.Select(value => value?.ToString())
                    ])));

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.True(
            response.StatusCode == status,
            $"Expected {(int)status}, got {(int)response.StatusCode}: {body}");
        var problem = await response.Content.ReadFromJsonAsync<Problem>(
            TestContext.Current.CancellationToken);
        Assert.Equal(code, problem!.Code);
    }

    private sealed record Envelope<T>(T Data);

    private sealed record Capabilities(
        bool LocalAutomationEnabled,
        IReadOnlyList<Provider> Providers);

    private sealed record Provider(string Id, string DisplayName);

    private sealed record Authorization(string AuthorizationUrl);

    private sealed record Problem(string Code);

    private sealed record SessionProjection(
        bool Authenticated,
        SessionMetadata? Session);

    private sealed record SessionMetadata(Guid Id);

    private sealed class ExternalOAuthWebApplicationFactory(
        PostgreSqlContainerFixture postgres,
        string environment = "Test",
        bool configureGoogle = false,
        TestDataProtectionCertificate? certificate = null)
        : WebApplicationFactory<Program>, IAsyncLifetime
    {
        internal static WebApplicationFactoryClientOptions OAuthClientOptions =>
            new()
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://accounts.example.test"),
                HandleCookies = true
            };

        private string _databaseName = string.Empty;
        private string _connectionString = string.Empty;

        internal FakeOAuthServer OAuth { get; } = new();

        public async ValueTask InitializeAsync()
        {
            (_databaseName, _connectionString) =
                await postgres.CreateDatabaseAsync(
                    TestContext.Current.CancellationToken);
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
                .Database.MigrateAsync(
                    TestContext.Current.CancellationToken);
            Services.GetRequiredService<CapturedLogProvider>().Clear();
        }

        internal HttpClient CreateOAuthClient() =>
            CreateClient(OAuthClientOptions);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            if (certificate is not null)
            {
                builder.UseSetting(
                    "DataProtection:CertificatePath",
                    certificate.Path);
                builder.UseSetting(
                    "DataProtection:CertificatePassword",
                    certificate.Password);
            }

            builder.UseSetting(
                "ExternalAuthentication:PublicOrigin",
                "https://accounts.example.test");
            builder.UseSetting(
                "ExternalAuthentication:Providers:GitHub:ClientId",
                "test-github-id");
            builder.UseSetting(
                "ExternalAuthentication:Providers:GitHub:ClientSecret",
                "test-github-secret");
            builder.UseSetting(
                "ExternalAuthentication:Providers:Yandex:ClientId",
                "test-yandex-id");
            builder.UseSetting(
                "ExternalAuthentication:Providers:Yandex:ClientSecret",
                "test-yandex-secret");
            if (configureGoogle)
            {
                builder.UseSetting(
                    "ExternalAuthentication:Providers:Google:ClientId",
                    "test-google-id");
                builder.UseSetting(
                    "ExternalAuthentication:Providers:Google:ClientSecret",
                    "test-google-secret");
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = _connectionString,
                    ["LocalAutomationAuth:Enabled"] = "true",
                    ["LocalAutomationAuth:CreateRateLimitPerMinute"] = "100",
                    ["LocalAutomationAuth:SignInRateLimitPerFiveMinutes"] =
                        "100",
                    ["ExternalAuthentication:PublicOrigin"] =
                        "https://accounts.example.test",
                    ["ExternalAuthentication:Providers:GitHub:ClientId"] =
                        "test-github-id",
                    ["ExternalAuthentication:Providers:GitHub:ClientSecret"] =
                        "test-github-secret",
                    ["ExternalAuthentication:Providers:Yandex:ClientId"] =
                        "test-yandex-id",
                    ["ExternalAuthentication:Providers:Yandex:ClientSecret"] =
                        "test-yandex-secret",
                    ["ExternalOAuthSecurity:ChallengePermitLimitPerMinute"] =
                        "200",
                    ["ExternalOAuthSecurity:CallbackPermitLimitPerFiveMinutes"] =
                        "200",
                    ["ExternalOAuthSecurity:CallbackConcurrencyLimit"] = "10",
                    ["Testing:AssumeHttpsBoundary"] = "true"
                };
                if (configureGoogle)
                {
                    settings[
                        "ExternalAuthentication:Providers:Google:ClientId"] =
                        "test-google-id";
                    settings[
                        "ExternalAuthentication:Providers:Google:ClientSecret"] =
                        "test-google-secret";
                }

                configuration.AddInMemoryCollection(settings);
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(OAuth);
                services.AddSingleton<CapturedLogProvider>();
                services.AddSingleton<ILoggerProvider>(
                    provider =>
                        provider.GetRequiredService<CapturedLogProvider>());
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (_databaseName.Length > 0)
            {
                await postgres.DropDatabaseAsync(
                    _databaseName,
                    CancellationToken.None);
            }
        }
    }

}
