using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using OpenIddict.Client.SystemNetHttp;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Template.Infrastructure.Authentication;

internal static class YandexOpenIddictClientIntegration
{
    private const string ProviderName = "yandex";

    private static readonly Uri Issuer =
        new("https://oauth.yandex.ru/");
    private static readonly Uri AuthorizationEndpoint =
        new("https://oauth.yandex.ru/authorize");
    private static readonly Uri TokenEndpoint =
        new("https://oauth.yandex.ru/token");
    private static readonly Uri UserInfoEndpoint =
        new("https://login.yandex.ru/info");

    internal static void Add(
        OpenIddictClientBuilder builder,
        string clientId,
        string clientSecret,
        Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentNullException.ThrowIfNull(redirectUri);

        builder.AddEventHandler(
            FormatCommaSeparatedScopes.Descriptor);
        builder.AddEventHandler(
            UseOAuthUserInfoAuthorizationScheme.Descriptor);

        var configuration = new OpenIddictConfiguration
        {
            Issuer = Issuer,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
            UserInfoEndpoint = UserInfoEndpoint
        };
        configuration.GrantTypesSupported.Add(GrantTypes.AuthorizationCode);
        configuration.ResponseTypesSupported.Add(ResponseTypes.Code);
        configuration.CodeChallengeMethodsSupported.Add(
            CodeChallengeMethods.Sha256);
        configuration.TokenEndpointAuthMethodsSupported.Add(
            ClientAuthenticationMethods.ClientSecretBasic);
        configuration.TokenEndpointAuthMethodsSupported.Add(
            ClientAuthenticationMethods.ClientSecretPost);

        var registration = new OpenIddictClientRegistration
        {
            RegistrationId = ProviderName,
            ProviderName = ProviderName,
            ProviderDisplayName = "Yandex",
            Issuer = Issuer,
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirectUri,
            Configuration = configuration,
            DisablePushedAuthorizationRequests = true
        };
        registration.Scopes.UnionWith(
            ["login:email", "login:info", "login:avatar"]);
        registration.GrantTypes.Add(GrantTypes.AuthorizationCode);
        registration.ResponseTypes.Add(ResponseTypes.Code);
        registration.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);

        builder.AddRegistration(registration);
    }

    private sealed class FormatCommaSeparatedScopes
        : IOpenIddictClientHandler<
            OpenIddictClientEvents.ProcessChallengeContext>
    {
        internal static OpenIddictClientHandlerDescriptor Descriptor { get; } =
            OpenIddictClientHandlerDescriptor
                .CreateBuilder<
                    OpenIddictClientEvents.ProcessChallengeContext>()
                .UseSingletonHandler<FormatCommaSeparatedScopes>()
                .SetOrder(
                    OpenIddictClientHandlers.AttachChallengeParameters
                        .Descriptor.Order + 750)
                .SetType(OpenIddictClientHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(
            OpenIddictClientEvents.ProcessChallengeContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (IsYandex(context.Registration))
            {
                context.Request.Scope = string.Join(",", context.Scopes);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class UseOAuthUserInfoAuthorizationScheme
        : IOpenIddictClientHandler<
            OpenIddictClientEvents.PrepareUserInfoRequestContext>
    {
        internal static OpenIddictClientHandlerDescriptor Descriptor { get; } =
            OpenIddictClientHandlerDescriptor
                .CreateBuilder<
                    OpenIddictClientEvents.PrepareUserInfoRequestContext>()
                .UseSingletonHandler<
                    UseOAuthUserInfoAuthorizationScheme>()
                .SetOrder(
                    OpenIddictClientSystemNetHttpHandlers.UserInfo
                        .AttachBearerAccessToken.Descriptor.Order + 250)
                .SetType(OpenIddictClientHandlerType.Custom)
                .Build();

        public ValueTask HandleAsync(
            OpenIddictClientEvents.PrepareUserInfoRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!IsYandex(context.Registration))
            {
                return ValueTask.CompletedTask;
            }

            var request = context.Transaction.GetHttpRequestMessage()
                ?? throw new InvalidOperationException(
                    "The Yandex user-info HTTP request is unavailable.");
            var parameter = request.Headers.Authorization?.Parameter;
            if (string.IsNullOrWhiteSpace(parameter))
            {
                throw new InvalidOperationException(
                    "The Yandex user-info access token is unavailable.");
            }

            request.Headers.Authorization =
                new AuthenticationHeaderValue("OAuth", parameter);
            return ValueTask.CompletedTask;
        }
    }

    private static bool IsYandex(
        OpenIddictClientRegistration registration) =>
        string.Equals(
            registration.ProviderName,
            ProviderName,
            StringComparison.Ordinal);
}
