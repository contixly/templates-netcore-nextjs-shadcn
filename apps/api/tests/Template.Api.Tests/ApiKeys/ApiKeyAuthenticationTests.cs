using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Template.Api.Tests.Infrastructure;
using Template.Api.Tests.Organizations;
using Template.Application.ApiKeys.Ports;
using Template.Domain.ApiKeys;
using Template.Infrastructure.Persistence;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeyAuthenticationTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>, IAsyncLifetime
{
    private const string MePath = "/api/v1/me";
    private const string MixedPath = "/api/v1/testing/consumer";

    public async ValueTask InitializeAsync() =>
        await factory.ResetAuthDataAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task AgedBrowserCookieDoesNotRenewOnMachineOnlyRouteWithoutHeader()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.FromUnixTimeSeconds(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await using var controlled = WithTimeProvider(time);
        using var client = CreateClient(controlled);
        await CreatePersonalKeyAsync(client);
        var before = await ReadOnlySessionAsync(controlled);
        time.Advance(TimeSpan.FromDays(4));

        using var response = await client.GetAsync(
            MePath,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "api_key_missing");
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.Equal(before, await ReadOnlySessionAsync(controlled));
    }

    [Fact]
    public async Task StaleBrowserCookieDoesNotDeleteOnMachineOnlyRouteWithoutHeader()
    {
        await using var isolated = factory.WithWebHostBuilder(_ => { });
        using var client = CreateClient(isolated);
        await CreatePersonalKeyAsync(client);
        await using (var scope = isolated.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var session = await db.Sessions.SingleAsync(
                TestContext.Current.CancellationToken);
            session.ProtectedTicket = [0x01, 0x02, 0x03, 0x04];
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var before = await ReadOnlySessionAsync(isolated);

        using var response = await client.GetAsync(
            MePath,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "api_key_missing");
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.Equal(before, await ReadOnlySessionAsync(isolated));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AgedBrowserCookieDoesNotRenewWhenHeaderSelectsApiKey(
        bool validKey)
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.FromUnixTimeSeconds(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await using var controlled = WithTimeProvider(time);
        using var client = CreateClient(controlled);
        var key = await CreatePersonalKeyAsync(client);
        var presented = validKey
            ? key.Credential
            : controlled.Services.GetRequiredService<IApiKeyCredentialService>()
                .Generate(ApiKeyOwnerKind.User).Credential;
        time.Advance(TimeSpan.FromDays(4));

        using var response = await SendWithApiKeyAsync(client, MePath, presented);

        if (validKey)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await AssertMeUsesKeyAsync(response, key.Id);
        }
        else
        {
            await AssertProblemAsync(
                response,
                HttpStatusCode.Unauthorized,
                "api_key_invalid");
        }
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    [InlineData("blank")]
    public async Task StaleBrowserCookieDoesNotDeleteCookieWhenHeaderSelectsApiKey(
        string presentation)
    {
        await using var isolated = factory.WithWebHostBuilder(_ => { });
        using var client = CreateClient(isolated);
        var key = await CreatePersonalKeyAsync(client);
        await using (var scope = isolated.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
                .Sessions.ExecuteDeleteAsync(
                    TestContext.Current.CancellationToken);
        }
        var presented = presentation switch
        {
            "valid" => key.Credential,
            "invalid" => isolated.Services
                .GetRequiredService<IApiKeyCredentialService>()
                .Generate(ApiKeyOwnerKind.User).Credential,
            "blank" => "   ",
            _ => throw new ArgumentOutOfRangeException(nameof(presentation))
        };

        using var response = await SendWithApiKeyAsync(client, MePath, presented);

        if (presentation == "valid")
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await AssertMeUsesKeyAsync(response, key.Id);
        }
        else
        {
            await AssertProblemAsync(
                response,
                HttpStatusCode.Unauthorized,
                presentation == "blank" ? "api_key_missing" : "api_key_invalid");
        }
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task CanonicalInjectedMachinePrincipalAuthorizesAndIsReadable()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            "/api/testing/api-key-principal/valid",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.GetProperty("authorized").GetBoolean());
        Assert.True(document.RootElement.GetProperty("readable").GetBoolean());
    }

    [Theory]
    [InlineData("duplicate-identity")]
    [InlineData("additional-identity")]
    [InlineData("unknown-claim")]
    [InlineData("duplicate-claim")]
    [InlineData("mixed-owner")]
    [InlineData("invalid-scope")]
    public async Task MalformedInjectedMachinePrincipalDeniesWithoutThrowing(
        string scenario)
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(
            $"/api/testing/api-key-principal/{scenario}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.False(document.RootElement.GetProperty("authorized").GetBoolean());
        Assert.False(document.RootElement.GetProperty("readable").GetBoolean());
    }

    [Fact]
    public async Task MachineRouteMapsMissingBlankAndBearerOnlyCredentialsToMissingWithoutStoreUse()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var missing = await browser.GetAsync(MePath, TestContext.Current.CancellationToken);
        using var blank = await SendWithApiKeyAsync(browser, MePath, string.Empty);
        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, MePath);
        bearerRequest.Headers.Authorization = new("Bearer", key.Credential);
        using var bearer = await browser.SendAsync(
            bearerRequest,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(missing, HttpStatusCode.Unauthorized, "api_key_missing");
        await AssertProblemAsync(blank, HttpStatusCode.Unauthorized, "api_key_missing");
        await AssertProblemAsync(bearer, HttpStatusCode.Unauthorized, "api_key_missing");
        await AssertNoCookieOrCredentialAsync(missing, key.Credential);
        await AssertNoCookieOrCredentialAsync(blank, key.Credential);
        await AssertNoCookieOrCredentialAsync(bearer, key.Credential);
        Assert.Equal(3, logs.Logs.Count(log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "me") &&
            Equals(log.State.GetValueOrDefault("MachineApiOutcome"), "missing")));
        AssertCredentialNotLogged(logs, key.Credential);
        Assert.Equal(0, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task MultipleAndMalformedHeaderValuesAreInvalidBeforeStoreConsumption()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();
        using var multipleRequest = new HttpRequestMessage(HttpMethod.Get, MePath);
        Assert.True(multipleRequest.Headers.TryAddWithoutValidation(
            "x-api-key",
            [key.Credential, key.Credential]));
        using var multiple = await browser.SendAsync(
            multipleRequest,
            TestContext.Current.CancellationToken);
        using var malformed = await SendWithApiKeyAsync(
            browser,
            MePath,
            $" {key.Credential}");

        await AssertProblemAsync(multiple, HttpStatusCode.Unauthorized, "api_key_invalid");
        await AssertProblemAsync(malformed, HttpStatusCode.Unauthorized, "api_key_invalid");
        await AssertNoCookieOrCredentialAsync(multiple, key.Credential);
        await AssertNoCookieOrCredentialAsync(malformed, key.Credential);
        AssertCredentialNotLogged(logs, key.Credential);
        var invalidAudits = logs.Logs.Where(log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "me") &&
            Equals(log.State.GetValueOrDefault("MachineApiOutcome"), "invalid"))
            .ToArray();
        Assert.Equal(2, invalidAudits.Length);
        Assert.All(invalidAudits, audit =>
        {
            Assert.Null(audit.State.GetValueOrDefault("OwnerKind"));
            Assert.Null(audit.State.GetValueOrDefault("OwnerId"));
            Assert.Null(audit.State.GetValueOrDefault("ApiKeyId"));
        });
        Assert.Equal(0, await RequestCountAsync(key.Id));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task UnknownAndInactiveCredentialsShareOneNonDisclosingInvalidResponse(
        string state)
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        var presented = key.Credential;
        if (state == "unknown")
        {
            presented = factory.Services
                .GetRequiredService<IApiKeyCredentialService>()
                .Generate(ApiKeyOwnerKind.User)
                .Credential;
        }
        else
        {
            await MutateKeyAsync(key.Id, row =>
            {
                if (state == "disabled")
                {
                    row.Enabled = false;
                }
                else if (state == "expired")
                {
                    row.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
                }
                else
                {
                    row.RevokedAt = DateTimeOffset.UtcNow;
                }
            });
        }

        using var response = await SendWithApiKeyAsync(browser, MePath, presented);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "api_key_invalid");
        await AssertNoCookieOrCredentialAsync(response, presented);
        Assert.Equal(0, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task RateLimitedCredentialReturnsBoundedRetryAfterAndDoesNotIssueCookie()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser, rateLimitMax: 1);
        using var machine = factory.CreateApiClient();
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var accepted = await SendWithApiKeyAsync(machine, MePath, key.Credential);
        using var limited = await SendWithApiKeyAsync(machine, MePath, key.Credential);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await AssertProblemAsync(
            limited,
            HttpStatusCode.TooManyRequests,
            "api_key_rate_limited");
        var retryAfter = Assert.Single(limited.Headers.GetValues("Retry-After"));
        Assert.True(int.TryParse(retryAfter, out var seconds));
        Assert.InRange(seconds, 1, 86_400);
        await AssertNoCookieOrCredentialAsync(accepted, key.Credential);
        await AssertNoCookieOrCredentialAsync(limited, key.Credential);
        var audit = Assert.Single(logs.Logs, log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "me") &&
            Equals(
                log.State.GetValueOrDefault("MachineApiOutcome"),
                "rate_limited"));
        Assert.Equal("user", audit.State.GetValueOrDefault("OwnerKind"));
        Assert.Equal(key.UserId, audit.State.GetValueOrDefault("OwnerId"));
        Assert.Equal(key.Id, audit.State.GetValueOrDefault("ApiKeyId"));
        var problemBody = await limited.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.DoesNotContain(key.Id.ToString("D"), problemBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key.UserId.ToString("D"), problemBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key.Start, problemBody, StringComparison.Ordinal);
        AssertCredentialNotLogged(logs, key.Credential);
        Assert.Equal(1, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task ValidCredentialCreatesOnlySafeMachineClaimsAndReturnsSafeMeProjection()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        using var machine = factory.CreateApiClient();
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var claimsResponse = await SendWithApiKeyAsync(
            machine,
            MixedPath,
            key.Credential);
        using var meResponse = await SendWithApiKeyAsync(
            machine,
            MePath,
            key.Credential);

        Assert.Equal(HttpStatusCode.OK, claimsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        using var claimsDocument = JsonDocument.Parse(
            await claimsResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var claimsRoot = claimsDocument.RootElement;
        Assert.Equal(
            "Template.ApiKey",
            claimsRoot.GetProperty("authenticationType").GetString());
        var claims = claimsRoot.GetProperty("claims").EnumerateArray()
            .Select(claim => (
                Type: claim.GetProperty("type").GetString()!,
                Value: claim.GetProperty("value").GetString()!))
            .ToArray();
        Assert.Equal(
            [
                ("urn:template:claim:api-key:id", key.Id.ToString("D")),
                ("urn:template:claim:api-key:owner-kind", "user"),
                ("urn:template:claim:api-key:scope", "basic:read"),
                ("urn:template:claim:api-key:start", key.Start),
                ("urn:template:claim:api-key:user-id", key.UserId.ToString("D"))
            ],
            claims);
        var claimsBody = claimsRoot.GetRawText();
        Assert.DoesNotContain(key.Credential, claimsBody, StringComparison.Ordinal);
        Assert.DoesNotContain("name", claimsBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", claimsBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rate", claimsBody, StringComparison.OrdinalIgnoreCase);

        using var meDocument = JsonDocument.Parse(
            await meResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var data = meDocument.RootElement.GetProperty("data");
        var principal = data.GetProperty("principal");
        var safeKey = data.GetProperty("key");
        Assert.Equal(
            ["key", "principal", "scopes"],
            data.EnumerateObject().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["organizationId", "ownerKind", "userId"],
            principal.EnumerateObject().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["configId", "id", "start"],
            safeKey.EnumerateObject().Select(property => property.Name)
                .Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("user", principal.GetProperty("ownerKind").GetString());
        Assert.Equal(key.UserId, principal.GetProperty("userId").GetGuid());
        Assert.Equal(JsonValueKind.Null, principal.GetProperty("organizationId").ValueKind);
        Assert.Equal(key.Id, safeKey.GetProperty("id").GetGuid());
        Assert.Equal(key.Start, safeKey.GetProperty("start").GetString());
        Assert.Equal("user-keys", safeKey.GetProperty("configId").GetString());
        Assert.Equal(
            ["basic:read"],
            data.GetProperty("scopes").EnumerateArray()
                .Select(scope => scope.GetString()!).ToArray());
        await AssertNoCookieOrCredentialAsync(claimsResponse, key.Credential);
        await AssertNoCookieOrCredentialAsync(meResponse, key.Credential);
        AssertCredentialNotLogged(logs, key.Credential);
        var audit = Assert.Single(logs.Logs, log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "me"));
        Assert.Equal("succeeded", audit.State.GetValueOrDefault("MachineApiOutcome"));
        Assert.Equal("user", audit.State.GetValueOrDefault("OwnerKind"));
        Assert.Equal(key.UserId, audit.State.GetValueOrDefault("OwnerId"));
        Assert.Equal(key.Id, audit.State.GetValueOrDefault("ApiKeyId"));
        Assert.Equal(2, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task OrganizationCredentialUsesOnlyOrganizationOwnerClaimsAndMeFields()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreateOrganizationKeyAsync(browser);
        using var machine = factory.CreateApiClient();

        using var claimsResponse = await SendWithApiKeyAsync(
            machine,
            MixedPath,
            key.Credential);
        using var meResponse = await SendWithApiKeyAsync(
            machine,
            MePath,
            key.Credential);

        Assert.Equal(HttpStatusCode.OK, claimsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        using var claimsDocument = JsonDocument.Parse(
            await claimsResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var claims = claimsDocument.RootElement.GetProperty("claims")
            .EnumerateArray()
            .Select(claim => (
                Type: claim.GetProperty("type").GetString()!,
                Value: claim.GetProperty("value").GetString()!))
            .ToArray();
        Assert.Equal(
            [
                ("urn:template:claim:api-key:id", key.Id.ToString("D")),
                ("urn:template:claim:api-key:organization-id", key.OrganizationId.ToString("D")),
                ("urn:template:claim:api-key:owner-kind", "organization"),
                ("urn:template:claim:api-key:scope", "basic:read"),
                ("urn:template:claim:api-key:scope", "organization:read"),
                ("urn:template:claim:api-key:start", key.Start)
            ],
            claims);

        using var meDocument = JsonDocument.Parse(
            await meResponse.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var data = meDocument.RootElement.GetProperty("data");
        var principal = data.GetProperty("principal");
        Assert.Equal("organization", principal.GetProperty("ownerKind").GetString());
        Assert.Equal(JsonValueKind.Null, principal.GetProperty("userId").ValueKind);
        Assert.Equal(
            key.OrganizationId,
            principal.GetProperty("organizationId").GetGuid());
        Assert.Equal(
            "org-keys",
            data.GetProperty("key").GetProperty("configId").GetString());
        Assert.Equal(
            ["basic:read", "organization:read"],
            data.GetProperty("scopes").EnumerateArray()
                .Select(scope => scope.GetString()!).ToArray());
        await AssertNoCookieOrCredentialAsync(claimsResponse, key.Credential);
        await AssertNoCookieOrCredentialAsync(meResponse, key.Credential);
        Assert.Equal(2, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task MachineOnlyMeRejectsAnOtherwiseValidBrowserSession()
    {
        using var browser = factory.CreateApiClient();
        await CreatePersonalKeyAsync(browser);

        using var response = await browser.GetAsync(
            MePath,
            TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "api_key_missing");
    }

    [Fact]
    public async Task MixedSelectorUsesBrowserWithoutHeaderAndMachineWithHeader()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        using var machine = factory.CreateApiClient();

        using var browserResponse = await browser.GetAsync(
            MixedPath,
            TestContext.Current.CancellationToken);
        using var machineResponse = await SendWithApiKeyAsync(
            machine,
            MixedPath,
            key.Credential);

        Assert.Equal(HttpStatusCode.OK, browserResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, machineResponse.StatusCode);
        Assert.Equal(
            "Identity.Application",
            await ReadAuthenticationTypeAsync(browserResponse));
        Assert.Equal(
            "Template.ApiKey",
            await ReadAuthenticationTypeAsync(machineResponse));
    }

    [Fact]
    public async Task SuppliedHeaderStrictlyOverridesAValidCookie()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        var unknown = factory.Services
            .GetRequiredService<IApiKeyCredentialService>()
            .Generate(ApiKeyOwnerKind.User)
            .Credential;

        using var invalid = await SendWithApiKeyAsync(browser, MixedPath, unknown);
        using var valid = await SendWithApiKeyAsync(browser, MixedPath, key.Credential);

        await AssertProblemAsync(invalid, HttpStatusCode.Unauthorized, "api_key_invalid");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal("Template.ApiKey", await ReadAuthenticationTypeAsync(valid));
    }

    [Fact]
    public async Task BrowserOnlyRouteDoesNotAuthenticateOrConsumeSuppliedApiKey()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);

        using var response = await SendWithApiKeyAsync(
            browser,
            "/api/v1/account/api-keys",
            key.Credential);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task AgedBrowserCookieStillRenewsOnBrowserOnlyRouteWithApiKeyHeader()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.FromUnixTimeSeconds(
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await using var controlled = WithTimeProvider(time);
        using var client = CreateClient(controlled);
        var key = await CreatePersonalKeyAsync(client);
        var before = await ReadOnlySessionAsync(controlled);
        time.Advance(TimeSpan.FromDays(4));

        using var response = await SendWithApiKeyAsync(
            client,
            "/api/v1/account/api-keys",
            key.Credential);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out _));
        var after = await ReadOnlySessionAsync(controlled);
        Assert.Equal(before.Id, after.Id);
        Assert.True(after.UpdatedAt > before.UpdatedAt);
        Assert.True(after.ExpiresAt > before.ExpiresAt);
        Assert.Equal(0, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task InvalidBrowserCookieStillClearsOnBrowserOnlyRouteWithApiKeyHeader()
    {
        await using var isolated = factory.WithWebHostBuilder(_ => { });
        using var client = CreateClient(isolated);
        var key = await CreatePersonalKeyAsync(client);
        await using (var scope = isolated.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var session = await db.Sessions.SingleAsync(
                TestContext.Current.CancellationToken);
            session.ProtectedTicket = [0x01, 0x02, 0x03, 0x04];
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var response = await SendWithApiKeyAsync(
            client,
            "/api/v1/account/api-keys",
            key.Credential);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out _));
        await using var verification = isolated.Services.CreateAsyncScope();
        Assert.Equal(
            0,
            await verification.ServiceProvider
                .GetRequiredService<TemplateDbContext>()
                .Sessions.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await RequestCountAsync(key.Id));
    }

    [Fact]
    public async Task ValidKeyWithoutRequiredScopeConsumesQuotaThenUsesMachineForbid()
    {
        using var browser = factory.CreateApiClient();
        var key = await CreatePersonalKeyAsync(browser);
        var logs = factory.Services.GetRequiredService<CapturedLogProvider>();
        logs.Clear();

        using var response = await SendWithApiKeyAsync(
            browser,
            $"{MixedPath}/organization-read",
            key.Credential);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "api_key_permission_denied");
        var audit = Assert.Single(logs.Logs, log =>
            Equals(log.State.GetValueOrDefault("MachineApiOperation"), "unknown") &&
            Equals(
                log.State.GetValueOrDefault("MachineApiOutcome"),
                "permission_denied"));
        Assert.Equal("user", audit.State.GetValueOrDefault("OwnerKind"));
        Assert.Equal(key.UserId, audit.State.GetValueOrDefault("OwnerId"));
        Assert.Equal(key.Id, audit.State.GetValueOrDefault("ApiKeyId"));
        AssertCredentialNotLogged(logs, key.Credential);
        Assert.Equal(1, await RequestCountAsync(key.Id));
    }

    private async Task<CreatedKey> CreatePersonalKeyAsync(
        HttpClient client,
        int rateLimitMax = 1000)
    {
        using var scenario = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Machine Key Owner",
                email = $"local-agent+machine-{Guid.NewGuid():N}@local-agent.test",
                password = "local-machine-key-owner-password"
            });
        var scenarioBody = await scenario.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);
        using var scenarioDocument = JsonDocument.Parse(scenarioBody);
        var userId = scenarioDocument.RootElement.GetProperty("data")
            .GetProperty("user").GetProperty("id").GetGuid();
        using var create = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            "/api/v1/account/api-keys",
            new
            {
                name = "Machine caller",
                presetIds = new[] { "basic-read" },
                expiresIn = "30d",
                rateLimitEnabled = true,
                rateLimitMax,
                rateLimitWindow = "1h"
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var data = await ApiKeyEndpointTests.ReadDataAsync(create);
        return new(
            data.GetProperty("id").GetGuid(),
            userId,
            data.GetProperty("start").GetString()!,
            data.GetProperty("key").GetString()!);
    }

    private async Task<CreatedOrganizationKey> CreateOrganizationKeyAsync(
        HttpClient client)
    {
        using var scenario = await LocalAuthTestClient.CreateScenarioAsync(
            client,
            new
            {
                name = "Organization Machine Key Owner",
                email = $"local-agent+org-machine-{Guid.NewGuid():N}@local-agent.test",
                password = "local-organization-machine-owner-password"
            });
        Assert.Equal(HttpStatusCode.Created, scenario.StatusCode);
        using var organization =
            await OrganizationEndpointTestSupport.CreateOrganizationAsync(
                client,
                "Machine API Organization");
        Assert.Equal(HttpStatusCode.Created, organization.StatusCode);
        var organizationId = (await ApiKeyEndpointTests.ReadDataAsync(organization))
            .GetProperty("id").GetGuid();
        using var create = await ApiKeyEndpointTests.SendJsonWithCsrfAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/organizations/{organizationId:D}/api-keys",
            new
            {
                name = "Organization machine caller",
                presetIds = new[] { "basic-read", "organization-read" },
                expiresIn = "30d",
                rateLimitEnabled = true,
                rateLimitMax = 1000,
                rateLimitWindow = "1h"
            });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var data = await ApiKeyEndpointTests.ReadDataAsync(create);
        return new(
            data.GetProperty("id").GetGuid(),
            organizationId,
            data.GetProperty("start").GetString()!,
            data.GetProperty("key").GetString()!);
    }

    private async Task MutateKeyAsync(
        Guid id,
        Action<Template.Infrastructure.ApiKeys.ApiKeyEntity> mutation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var row = await db.ApiKeys.SingleAsync(
            key => key.Id == id,
            TestContext.Current.CancellationToken);
        mutation(row);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> RequestCountAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .ApiKeys.Where(key => key.Id == id)
            .Select(key => key.RequestCount)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> SendWithApiKeyAsync(
        HttpClient client,
        string path,
        string credential)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        Assert.True(request.Headers.TryAddWithoutValidation("x-api-key", credential));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private WebApplicationFactory<Program> WithTimeProvider(
        MutableTimeProvider time) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                services.PostConfigureAll<CookieAuthenticationOptions>(options =>
                    options.TimeProvider = time);
            }));

    private static async Task<SessionLifecycle> ReadOnlySessionAsync(
        WebApplicationFactory<Program> application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .Sessions.AsNoTracking()
            .Select(session => new SessionLifecycle(
                session.Id,
                session.UpdatedAt,
                session.ExpiresAt,
                Convert.ToHexString(session.ProtectedTicket)))
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });

    private static async Task<string?> ReadAuthenticationTypeAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("authenticationType").GetString();
    }

    private static async Task AssertMeUsesKeyAsync(
        HttpResponseMessage response,
        Guid keyId)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            keyId,
            document.RootElement.GetProperty("data")
                .GetProperty("key")
                .GetProperty("id")
                .GetGuid());
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken));
        var problem = document.RootElement;
        var expected = code switch
        {
            "api_key_missing" => ("API key required", "An API key is required to access this resource."),
            "api_key_invalid" => ("API key invalid", "The supplied API key is invalid."),
            "api_key_permission_denied" => ("API key permission denied", "You do not have permission to perform this API key operation."),
            "api_key_rate_limited" => ("API key rate limited", "The API key rate limit was exceeded."),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal($"urn:template:problem:{code}", problem.GetProperty("type").GetString());
        Assert.Equal(expected.Item1, problem.GetProperty("title").GetString());
        Assert.Equal(expected.Item2, problem.GetProperty("detail").GetString());
        Assert.Equal(response.RequestMessage!.RequestUri!.AbsolutePath, problem.GetProperty("instance").GetString());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    private static async Task AssertNoCookieOrCredentialAsync(
        HttpResponseMessage response,
        string credential)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.DoesNotContain(
            credential,
            await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    private static void AssertCredentialNotLogged(
        CapturedLogProvider logs,
        string credential)
    {
        var rendered = string.Join('\n', logs.Logs.Select(log =>
            $"{log.Message} {JsonSerializer.Serialize(log.State)} " +
            JsonSerializer.Serialize(log.Scope)));
        Assert.DoesNotContain(credential, rendered, StringComparison.Ordinal);
    }

    private sealed record CreatedKey(
        Guid Id,
        Guid UserId,
        string Start,
        string Credential);

    private sealed record CreatedOrganizationKey(
        Guid Id,
        Guid OrganizationId,
        string Start,
        string Credential);

    private sealed record SessionLifecycle(
        Guid Id,
        DateTimeOffset UpdatedAt,
        DateTimeOffset ExpiresAt,
        string ProtectedTicket);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan value) => _utcNow += value;
    }
}
