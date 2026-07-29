using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;

namespace Template.Api.Tests.Infrastructure;

internal sealed class FakeOAuthServer : IHttpClientFactory
{
    private readonly FakeOAuthHandler _handler = new();

    internal IReadOnlyCollection<FakeOAuthRequest> Requests =>
        _handler.Requests;

    public HttpClient CreateClient(string name) =>
        new(_handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://api.github.com/", UriKind.Absolute)
        };

    private sealed class FakeOAuthHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<FakeOAuthRequest> _requests = [];

        internal IReadOnlyCollection<FakeOAuthRequest> Requests =>
            _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var form = QueryHelpers.ParseQuery($"?{body}");
            var code = form.TryGetValue(
                    OpenIddictConstants.Parameters.Code,
                    out var values)
                ? values.ToString()
                : string.Empty;
            _requests.Enqueue(new FakeOAuthRequest(
                request.Method,
                request.RequestUri ??
                throw new InvalidOperationException(
                    "The fake OAuth request URI is missing.")));

            var response = request.RequestUri switch
            {
                { Host: "oauth.yandex.ru", AbsolutePath: "/token" } =>
                    Json(
                        $$"""
                        {
                          "access_token": "ephemeral-yandex-token-{{code}}",
                          "token_type": "bearer",
                          "expires_in": 3600
                        }
                        """),
                { Host: "login.yandex.ru", AbsolutePath: "/info" } =>
                    YandexProfile(
                        request.Headers.Authorization?.Parameter ??
                        string.Empty),
                { Host: "github.com", AbsolutePath: "/login/oauth/access_token" } =>
                    Json(
                        $$"""
                        {
                          "access_token": "ephemeral-github-token-{{code}}",
                          "token_type": "bearer",
                          "scope": "user:email"
                        }
                        """),
                { Host: "api.github.com", AbsolutePath: "/user" } =>
                    Json(
                        """
                        {
                          "id": 424242,
                          "login": "unverified-owner",
                          "name": "Unverified Owner",
                          "avatar_url": "https://avatars.example.test/424242"
                        }
                        """),
                { Host: "api.github.com", AbsolutePath: "/user/emails" } =>
                    Json(
                        """
                        [
                          {
                            "email": "unverified-owner@example.test",
                            "primary": true,
                            "verified": false,
                            "visibility": "private"
                          }
                        ]
                        """),
                _ => throw new InvalidOperationException(
                    $"Unexpected fake OAuth request: {request.Method} " +
                    request.RequestUri)
            };
            response.RequestMessage = request;
            return response;
        }

        private static HttpResponseMessage YandexProfile(string accessToken)
        {
            var code = accessToken.StartsWith(
                    "ephemeral-yandex-token-",
                    StringComparison.Ordinal)
                ? accessToken["ephemeral-yandex-token-".Length..]
                : string.Empty;
            return code switch
            {
                "missing-email" => Json(
                    """
                    {
                      "id": "missing-email-subject",
                      "display_name": "Missing Email"
                    }
                    """),
                "existing-subject-owner" => Json(
                    """
                    {
                      "id": "shared-provider-subject",
                      "default_email": "external-owner@example.test",
                      "display_name": "External Owner"
                    }
                    """),
                "existing-subject-other-email" => Json(
                    """
                    {
                      "id": "shared-provider-subject",
                      "default_email": "local-agent+other-owner@local-agent.test",
                      "display_name": "Other Owner"
                    }
                    """),
                "connect-success" => Json(
                    """
                    {
                      "id": "connect-success-subject",
                      "default_email": "connected-secondary@example.test",
                      "display_name": "Connected Profile",
                      "default_avatar_id": "connected-avatar"
                    }
                    """),
                "audit-code" => Json(
                    """
                    {
                      "id": "audit-subject-991",
                      "default_email": "audit-owner@example.test",
                      "display_name": "Audit Owner"
                    }
                    """),
                _ => Json(
                    $$"""
                    {
                      "id": "subject-{{code}}",
                      "default_email": "{{code}}@example.test",
                      "display_name": "OAuth Owner",
                      "default_avatar_id": "avatar-{{code}}"
                    }
                    """)
            };
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
}

internal sealed record FakeOAuthRequest(HttpMethod Method, Uri Uri);
