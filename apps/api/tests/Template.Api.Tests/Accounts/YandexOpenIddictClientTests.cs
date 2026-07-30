using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;
using Template.Api.Endpoints;
using Template.Api.Tests.Infrastructure;
using Template.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Api.Tests.Accounts;

public sealed class YandexOpenIddictClientTests(
    PostgreSqlContainerFixture postgres)
    : IAsyncLifetime
{
    private YandexOAuthWebApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new YandexOAuthWebApplicationFactory(postgres);
        await _factory.InitializeAsync();
    }

    public async ValueTask DisposeAsync() =>
        await _factory.DisposeAsync();

    [Fact]
    public async Task AuthorizationRequestUsesS256AndCommaSeparatedScopes()
    {
        using var client = _factory.CreateOAuthClient();

        using var response = await client.GetAsync(
            YandexTestEndpointModule.ChallengeUri,
            TestContext.Current.CancellationToken);

        Assert.True(
            response.StatusCode == HttpStatusCode.Redirect,
            await _factory.DescribeFailureAsync(response));
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.Equal("https", location.Scheme);
        Assert.Equal("oauth.yandex.ru", location.Host);
        Assert.Equal("/authorize", location.AbsolutePath);

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal(
            "https://accounts.example.test/api/auth/oauth2/callback/yandex",
            query["redirect_uri"].ToString());
        Assert.Equal(
            CodeChallengeMethods.Sha256,
            query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(
            query["code_challenge"].ToString()));

        var scope = query["scope"].ToString();
        Assert.DoesNotContain(' ', scope);
        Assert.Equal(
            ["login:avatar", "login:email", "login:info"],
            scope.Split(',').Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Scopes.OfflineAccess, scope, StringComparison.Ordinal);

        var state = query["state"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(state));
        Assert.NotEqual(3, state.Split('.').Length);

        await using var scopeHandle = _factory.Services.CreateAsyncScope();
        var stateRows = await scopeHandle.ServiceProvider
            .GetRequiredService<AuthDbContext>()
            .OpenIddictTokens
            .AsNoTracking()
            .CountAsync(
                token => token.Type == TokenTypeIdentifiers.Private.StateToken,
                TestContext.Current.CancellationToken);
        Assert.Equal(1, stateRows);
    }

    [Fact]
    public async Task ConcurrentCallbacksRedeemStateOnceAndWinnerSendsDocumentedRequests()
    {
        using var client = _factory.CreateOAuthClient();
        using var challenge = await client.GetAsync(
            YandexTestEndpointModule.ChallengeUri,
            TestContext.Current.CancellationToken);
        Assert.True(
            challenge.StatusCode == HttpStatusCode.Redirect,
            await _factory.DescribeFailureAsync(challenge));
        var authorizationUri = Assert.IsType<Uri>(challenge.Headers.Location);
        var state = QueryHelpers.ParseQuery(authorizationUri.Query)["state"]
            .ToString();
        Assert.False(string.IsNullOrWhiteSpace(state));
        var callback = new Uri(
            $"https://accounts.example.test{YandexTestEndpointModule.CallbackPath}?code=test-code&state={Uri.EscapeDataString(state)}");

        var first = client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        var second = client.GetAsync(
            callback,
            TestContext.Current.CancellationToken);
        var responses = await Task.WhenAll(first, second);
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var outcomes = await Task.WhenAll(
            ReadOutcomeAsync(firstResponse),
            ReadOutcomeAsync(secondResponse));

        Assert.Single(outcomes, outcome => outcome.Succeeded);
        var rejected = Assert.Single(
            outcomes,
            outcome => !outcome.Succeeded);
        Assert.Equal(OpenIddictConstants.Errors.InvalidToken, rejected.Error);
        Assert.Equal(1, _factory.StateReplayProbe.RedeemedPasses);

        Assert.Equal(2, _factory.RemoteRequests.Requests.Count);
        var token = Assert.Single(
            _factory.RemoteRequests.Requests,
            request =>
                request.Uri == new Uri("https://oauth.yandex.ru/token"));
        Assert.Equal(HttpMethod.Post, token.Method);
        Assert.Equal(
            "application/x-www-form-urlencoded",
            token.ContentType);
        Assert.Null(token.AuthorizationScheme);
        Assert.False(token.HasAuthorizationParameter);
        Assert.True(token.HasClientSecretParameter);
        Assert.True(token.HasNonEmptyClientSecretParameter);
        Assert.Equal(
            ["client_id", "code", "code_verifier", "grant_type", "redirect_uri"],
            token.FormParameters.Keys.Order(StringComparer.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(
            token.FormParameters["client_id"]));
        Assert.Equal(
            GrantTypes.AuthorizationCode,
            token.FormParameters["grant_type"]);
        Assert.Equal("test-code", token.FormParameters["code"]);
        Assert.Equal(
            $"https://accounts.example.test{YandexTestEndpointModule.CallbackPath}",
            token.FormParameters["redirect_uri"]);

        var verifier = token.FormParameters["code_verifier"];
        Assert.False(string.IsNullOrWhiteSpace(verifier));
        var actualChallenge = WebEncoders.Base64UrlEncode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var expectedChallenge =
            QueryHelpers.ParseQuery(authorizationUri.Query)["code_challenge"]
                .ToString();
        Assert.Equal(expectedChallenge, actualChallenge);

        var userInfo = Assert.Single(
            _factory.RemoteRequests.Requests,
            request =>
                request.Uri == new Uri("https://login.yandex.ru/info"));
        Assert.Equal(HttpMethod.Get, userInfo.Method);
        Assert.Equal("OAuth", userInfo.AuthorizationScheme);
        Assert.True(userInfo.HasAuthorizationParameter);
        Assert.Empty(userInfo.FormParameters);
        Assert.False(userInfo.HasClientSecretParameter);
        Assert.DoesNotContain(
            "oauth_token",
            userInfo.Uri.Query,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CallbackOutcome> ReadOutcomeAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        if (!string.Equals(
                response.Content.Headers.ContentType?.MediaType,
                "application/json",
                StringComparison.OrdinalIgnoreCase))
        {
            var error = body
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(':', 2))
                .FirstOrDefault(parts =>
                    parts.Length is 2
                    && parts[0] == "error");
            var description = body
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(':', 2))
                .FirstOrDefault(parts =>
                    parts.Length is 2
                    && parts[0] == "error_description");
            return new CallbackOutcome(
                false,
                error?[1],
                description?[1]);
        }

        try
        {
            return JsonSerializer.Deserialize<CallbackOutcome>(
                    body,
                    JsonSerializerOptions.Web)
                ?? throw new JsonException("The callback outcome was null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Callback returned {(int)response.StatusCode}: {body}",
                exception);
        }
    }

    private sealed record CallbackOutcome(
        bool Succeeded,
        string? Error,
        string? Description);

    private sealed class YandexOAuthWebApplicationFactory(
        PostgreSqlContainerFixture postgres)
        : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private string _databaseName = string.Empty;
        private string _connectionString = string.Empty;

        internal CapturingRemoteRequestFactory RemoteRequests { get; } = new();

        internal StateReplayProbe StateReplayProbe { get; } = new();

        internal async Task<string> DescribeFailureAsync(
            HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);
            var failures = Services.GetRequiredService<CapturedLogProvider>()
                .Logs
                .Where(log =>
                    log.Level >= LogLevel.Error
                    || log.Exception is not null)
                .Select(log =>
                    $"{log.Category}: {log.Message}{Environment.NewLine}{log.Exception}");
            return string.Join(Environment.NewLine, [body, .. failures]);
        }

        public async ValueTask InitializeAsync()
        {
            (_databaseName, _connectionString) =
                await postgres.CreateDatabaseAsync(
                    TestContext.Current.CancellationToken);
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
                .Database.MigrateAsync(
                    TestContext.Current.CancellationToken);
            Services.GetRequiredService<CapturedLogProvider>().Clear();
        }

        internal HttpClient CreateOAuthClient() =>
            CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true
            });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Test");
            builder.UseSetting(
                "ExternalAuthentication:PublicOrigin",
                "https://accounts.example.test");
            builder.UseSetting(
                "ExternalAuthentication:Providers:Yandex:ClientId",
                "test-yandex-id");
            builder.UseSetting(
                "ExternalAuthentication:Providers:Yandex:ClientSecret",
                "test-yandex-secret");
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddFilter<CapturedLogProvider>(
                    level => level >= LogLevel.Debug);
            });
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Postgres"] = _connectionString,
                        ["ExternalAuthentication:PublicOrigin"] =
                            "https://accounts.example.test",
                        ["ExternalAuthentication:Providers:Yandex:ClientId"] =
                            "test-yandex-id",
                        ["ExternalAuthentication:Providers:Yandex:ClientSecret"] =
                            "test-yandex-secret",
                        ["Testing:AssumeHttpsBoundary"] = "true"
                    }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(RemoteRequests);
                services.AddSingleton<CapturedLogProvider>();
                services.AddSingleton<ILoggerProvider>(
                    provider =>
                        provider.GetRequiredService<CapturedLogProvider>());
                services.AddSingleton(StateReplayProbe);
                services.AddScoped<SynchronizeStateRedemption>();
                services.AddSingleton<ObserveStateRedemption>();
                services.Configure<OpenIddictClientOptions>(options =>
                {
                    options.Handlers.Add(
                        SynchronizeStateRedemption.Descriptor);
                    options.Handlers.Add(ObserveStateRedemption.Descriptor);
                });
                services.RemoveAll<IEndpointModule>();
                services.AddSingleton<IEndpointModule>(
                    new YandexTestEndpointModule());
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

    private sealed class YandexTestEndpointModule : IEndpointModule
    {
        internal const string ChallengePath =
            "/api/testing/yandex/challenge";
        internal static readonly Uri ChallengeUri = new(
            $"https://accounts.example.test{ChallengePath}");
        internal const string CallbackPath =
            "/api/auth/oauth2/callback/yandex";

        public void MapEndpoints(EndpointRouteContext context)
        {
            context.Root.MapGet(ChallengePath, ChallengeAsync)
                .AllowAnonymous()
                .ExcludeFromDescription();
            context.Root.MapMethods(
                    CallbackPath,
                    [HttpMethods.Get, HttpMethods.Post],
                    AuthenticateCallbackAsync)
                .AllowAnonymous()
                .ExcludeFromDescription();
        }

        private static Task ChallengeAsync(HttpContext context) =>
            context.ChallengeAsync(
                "yandex",
                new AuthenticationProperties());

        private static async Task AuthenticateCallbackAsync(
            HttpContext context)
        {
            var result = await context.AuthenticateAsync(
                OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
            var error = context.GetOpenIddictClientResponse()?.Error
                ?? FindProtocolException(result.Failure)?.Error;
            context.Response.StatusCode = result.Succeeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(
                new CallbackOutcome(result.Succeeded, error, null),
                context.RequestAborted);
        }

        private static OpenIddictExceptions.ProtocolException?
            FindProtocolException(Exception? exception)
        {
            while (exception is not null)
            {
                if (exception is OpenIddictExceptions.ProtocolException protocol)
                {
                    return protocol;
                }

                exception = exception.InnerException;
            }

            return null;
        }
    }

    private sealed class SynchronizeStateRedemption(
        IOpenIddictTokenManager tokens,
        StateReplayProbe probe)
        : IOpenIddictClientHandler<
            OpenIddictClientEvents.ProcessAuthenticationContext>
    {
        internal static OpenIddictClientHandlerDescriptor Descriptor { get; } =
            OpenIddictClientHandlerDescriptor
                .CreateBuilder<
                    OpenIddictClientEvents.ProcessAuthenticationContext>()
                .UseScopedHandler<SynchronizeStateRedemption>()
                .SetOrder(
                    OpenIddictClientHandlers.RedeemStateTokenEntry
                        .Descriptor.Order - 500)
                .SetType(OpenIddictClientHandlerType.Custom)
                .Build();

        public async ValueTask HandleAsync(
            OpenIddictClientEvents.ProcessAuthenticationContext context)
        {
            if (context.EndpointType
                    != OpenIddictClientEndpointType.Redirection
                || context.StateTokenPrincipal is null)
            {
                return;
            }

            var identifier = context.StateTokenPrincipal.GetTokenId()
                ?? throw new InvalidOperationException(
                    "The state token has no persistence identifier.");
            _ = await tokens.FindByIdAsync(
                identifier,
                context.CancellationToken);
            await probe.SynchronizeAsync(context.CancellationToken);
        }
    }

    private sealed class ObserveStateRedemption(StateReplayProbe probe)
        : IOpenIddictClientHandler<
            OpenIddictClientEvents.ProcessAuthenticationContext>
    {
        internal static OpenIddictClientHandlerDescriptor Descriptor { get; } =
            OpenIddictClientHandlerDescriptor
                .CreateBuilder<
                    OpenIddictClientEvents.ProcessAuthenticationContext>()
                .UseSingletonHandler<ObserveStateRedemption>()
                .SetOrder(
                    OpenIddictClientHandlers.RedeemStateTokenEntry
                        .Descriptor.Order + 500)
                .SetType(OpenIddictClientHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(
            OpenIddictClientEvents.ProcessAuthenticationContext context)
        {
            if (context.EndpointType
                == OpenIddictClientEndpointType.Redirection)
            {
                probe.RecordSuccessfulRedemption();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class StateReplayProbe
    {
        private readonly TaskCompletionSource _bothCallbacksReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private int _redeemedPasses;

        internal int RedeemedPasses =>
            Volatile.Read(ref _redeemedPasses);

        internal async Task SynchronizeAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
            {
                _bothCallbacksReady.TrySetResult();
            }

            await _bothCallbacksReady.Task.WaitAsync(cancellationToken);
        }

        internal void RecordSuccessfulRedemption() =>
            Interlocked.Increment(ref _redeemedPasses);
    }

    private sealed class CapturingRemoteRequestFactory
        : IHttpClientFactory
    {
        private readonly CapturingRemoteRequestHandler _handler = new();

        internal IReadOnlyCollection<CapturedRemoteRequest> Requests =>
            _handler.Requests;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false);
    }

    private sealed class CapturingRemoteRequestHandler
        : HttpMessageHandler
    {
        private readonly ConcurrentQueue<CapturedRemoteRequest> _requests = [];

        internal IReadOnlyCollection<CapturedRemoteRequest> Requests =>
            _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = QueryHelpers.ParseQuery(body);
            var hasClientSecret = form.TryGetValue(
                Parameters.ClientSecret,
                out var clientSecret);
            _requests.Enqueue(new CapturedRemoteRequest(
                request.Method,
                request.RequestUri
                    ?? throw new InvalidOperationException(
                        "The outbound request URI is missing."),
                request.Headers.Authorization?.Scheme,
                !string.IsNullOrWhiteSpace(
                    request.Headers.Authorization?.Parameter),
                request.Content?.Headers.ContentType?.MediaType,
                form
                    .Where(parameter =>
                        !string.Equals(
                            parameter.Key,
                            Parameters.ClientSecret,
                            StringComparison.Ordinal))
                    .ToDictionary(
                        parameter => parameter.Key,
                        parameter => parameter.Value.ToString(),
                        StringComparer.Ordinal),
                hasClientSecret,
                hasClientSecret
                    && !string.IsNullOrWhiteSpace(clientSecret.ToString())));

            var response = request.RequestUri switch
            {
                { Host: "oauth.yandex.ru", AbsolutePath: "/token" } =>
                    Json(
                        """
                        {
                          "access_token": "ephemeral-yandex-token",
                          "token_type": "bearer",
                          "expires_in": 3600
                        }
                        """),
                { Host: "login.yandex.ru", AbsolutePath: "/info" } =>
                    Json(
                        """
                        {
                          "id": "1000034426",
                          "default_email": "owner@example.test",
                          "display_name": "Yandex Owner",
                          "default_avatar_id": "131652443"
                        }
                        """),
                _ => throw new InvalidOperationException(
                    "An unexpected remote endpoint was requested.")
            };
            response.RequestMessage = request;
            return response;
        }

        private static HttpResponseMessage Json(string value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    value,
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed record CapturedRemoteRequest(
        HttpMethod Method,
        Uri Uri,
        string? AuthorizationScheme,
        bool HasAuthorizationParameter,
        string? ContentType,
        IReadOnlyDictionary<string, string> FormParameters,
        bool HasClientSecretParameter,
        bool HasNonEmptyClientSecretParameter);
}
