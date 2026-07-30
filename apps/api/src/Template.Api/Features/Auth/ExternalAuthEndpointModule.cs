using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore;
using OpenIddict.Client.AspNetCore;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;
using Template.Application.Accounts;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using Template.Infrastructure.Authentication;

namespace Template.Api.Features.Auth;

internal sealed class ExternalAuthEndpointModule : IEndpointModule
{
    private const string SignInFallback = "/dashboard";
    private const string ConnectFallback = "/user/connections";
    private const string ErrorPath = "/auth/error?code=";

    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedApi.MapPost(
                "/auth/external/{provider}/challenge",
                ChallengeAsync)
            .AllowAnonymous()
            .WithName("ChallengeExternalAuth")
            .AcceptsManuallyReadJson<ExternalAuthChallengeRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.ExternalOAuthChallenge)
            .Produces<ApiResponse<ExternalAuthChallengeResponse>>()
            .ProducesBadRequestVariants()
            .ProducesProtectedApiProblems();

        MapCallback(
            context,
            ExternalProvider.Google,
            "/api/auth/callback/google");
        MapCallback(
            context,
            ExternalProvider.GitHub,
            "/api/auth/callback/github");
        MapCallback(
            context,
            ExternalProvider.GitLab,
            "/api/auth/callback/gitlab");
        MapCallback(
            context,
            ExternalProvider.Vk,
            "/api/auth/callback/vk");
        MapCallback(
            context,
            ExternalProvider.Yandex,
            "/api/auth/oauth2/callback/yandex");
    }

    private static void MapCallback(
        EndpointRouteContext context,
        ExternalProvider provider,
        string path)
    {
        context.Root.MapMethods(
                path,
                [HttpMethods.Get, HttpMethods.Post],
                (
                    HttpContext http,
                    ExternalIdentityService identities,
                    IExternalIdentityNormalizer normalizer,
                    IBrowserSessionGateway sessions,
                    ILogger<ExternalAuthEndpointModule> logger,
                    CancellationToken cancellationToken) =>
                    CallbackAsync(
                        provider,
                        http,
                        identities,
                        normalizer,
                        sessions,
                        logger,
                        cancellationToken))
            .AllowAnonymous()
            .RequireRateLimiting(AuthRateLimitPolicies.ExternalOAuthCallback)
            .ExcludeFromDescription();
    }

    private static async Task<IResult> ChallengeAsync(
        string provider,
        ApiJsonRequestReader reader,
        IExternalProviderCatalog providers,
        IBrowserSessionGateway sessions,
        ExternalOAuthChallengeService challenges,
        ILogger<ExternalAuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        if (!ExternalProvider.TryParse(provider, out var externalProvider)
            || !providers.IsConfigured(externalProvider))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.ExternalProviderNotConfigured);
        }

        var request = await reader.ReadAsync<ExternalAuthChallengeRequest>(
            http,
            emptyBodyFactory: null,
            cancellationToken);
        var intent = request.Intent ??
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["intent"] = ["External OAuth intent is required."]
                });
        var current = await sessions.GetCurrentAsync(cancellationToken);
        if (intent == ExternalAuthIntent.SignIn && current is not null)
        {
            throw new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.AlreadyAuthenticated);
        }

        if (intent == ExternalAuthIntent.Connect && current is null)
        {
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        }

        var fallback = intent == ExternalAuthIntent.SignIn
            ? SignInFallback
            : ConnectFallback;
        if (!SafeReturnUrl.TryNormalize(
                request.ReturnUrl,
                fallback,
                out var returnPath))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidReturnUrl);
        }

        var authorizationUrl =
            await challenges.CreateAuthorizationUrlAsync(
                http,
                externalProvider,
                intent,
                returnPath,
                current,
                providers.GetAuthenticationScheme(externalProvider),
                cancellationToken);
        ExternalAuthSecurityEvents.Write(
            logger,
            "challenge",
            externalProvider.Value,
            "started",
            CorrelationIdMiddleware.GetTraceId(http),
            current?.User.Id.Value);
        return Results.Ok(new ApiResponse<ExternalAuthChallengeResponse>(
            new(authorizationUrl)));
    }

    private static async Task<IResult> CallbackAsync(
        ExternalProvider routeProvider,
        HttpContext http,
        ExternalIdentityService identities,
        IExternalIdentityNormalizer normalizer,
        IBrowserSessionGateway sessions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        try
        {
            if (!string.IsNullOrEmpty(
                    http.GetOpenIddictClientResponse()?.Error))
            {
                return CallbackFailure(
                    http,
                    logger,
                    routeProvider,
                    ApiProblemCodes.ExternalAuthFailed);
            }

            var authentication = await http.AuthenticateAsync(
                OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
            if (!authentication.Succeeded
                || authentication.Principal is null
                || authentication.Properties is null)
            {
                return CallbackFailure(
                    http,
                    logger,
                    routeProvider,
                    ApiProblemCodes.ExternalAuthFailed);
            }

            var properties = authentication.Properties;
            if (!properties.Items.TryGetValue(
                    ExternalOAuthStateProperties.Provider,
                    out var storedProvider)
                || !string.Equals(
                    storedProvider,
                    routeProvider.Value,
                    StringComparison.Ordinal)
                || !properties.Items.TryGetValue(
                    ExternalOAuthStateProperties.Intent,
                    out var storedIntent)
                || !ExternalOAuthStateProperties.TryParseIntent(
                    storedIntent,
                    out var intent)
                || !properties.Items.TryGetValue(
                    ExternalOAuthStateProperties.ReturnPath,
                    out var returnPath)
                || !SafeReturnUrl.TryNormalize(
                    returnPath,
                    intent == ExternalAuthIntent.SignIn
                        ? SignInFallback
                        : ConnectFallback,
                    out var normalizedReturnPath)
                || !string.Equals(
                    normalizedReturnPath,
                    returnPath,
                    StringComparison.Ordinal))
            {
                return CallbackFailure(
                    http,
                    logger,
                    routeProvider,
                    ApiProblemCodes.ExternalAuthFailed);
            }

            var ephemeralTokens = properties.GetTokens()
                .Where(token => !string.IsNullOrEmpty(token.Value))
                .ToDictionary(
                    token => token.Name,
                    token => token.Value!,
                    StringComparer.Ordinal);
            properties.StoreTokens([]);

            ExternalIdentityResult normalized;
            try
            {
                normalized = await normalizer.NormalizeAsync(
                    routeProvider,
                    authentication.Principal,
                    ephemeralTokens,
                    cancellationToken);
            }
            finally
            {
                ephemeralTokens.Clear();
            }

            if (!normalized.Succeeded)
            {
                return CallbackFailure(
                    http,
                    logger,
                    routeProvider,
                    MapNormalizerFailure(normalized.Failure!.Value));
            }

            AuthenticatedSession? current = null;
            if (intent == ExternalAuthIntent.Connect)
            {
                current = await sessions.GetCurrentAsync(cancellationToken);
                if (current is null
                    || !TryParseBoundContext(
                        properties,
                        out var userId,
                        out var sessionId)
                    || current.User.Id != userId
                    || current.Session.Id != sessionId)
                {
                    return CallbackFailure(
                        http,
                        logger,
                        routeProvider,
                        ApiProblemCodes.OAuthFlowContextChanged);
                }
            }

            var reconciliation = await identities.ReconcileAsync(
                normalized.Identity!,
                intent,
                current,
                cancellationToken);
            if (reconciliation.Failure is not null)
            {
                return CallbackFailure(
                    http,
                    logger,
                    routeProvider,
                    MapReconciliationFailure(
                        reconciliation.Failure!.Value));
            }

            var value = reconciliation.Value!;
            if (intent == ExternalAuthIntent.SignIn)
            {
                await sessions.SignInAsync(
                    value.User,
                    value.Provider.Value,
                    cancellationToken);
            }
            else
            {
                await sessions.RenewCurrentAsync(cancellationToken);
            }

            ExternalAuthSecurityEvents.Write(
                logger,
                intent == ExternalAuthIntent.SignIn
                    ? "sign_in"
                    : "connect",
                routeProvider.Value,
                "succeeded",
                CorrelationIdMiddleware.GetTraceId(http),
                value.User.Id.Value);
            return Results.Redirect(normalizedReturnPath);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CallbackFailure(
                http,
                logger,
                routeProvider,
                ApiProblemCodes.ExternalAuthFailed);
        }
    }

    private static bool TryParseBoundContext(
        AuthenticationProperties properties,
        out UserId userId,
        out SessionId sessionId)
    {
        userId = default;
        sessionId = default;
        if (!properties.Items.TryGetValue(
                ExternalOAuthStateProperties.UserId,
                out var rawUserId)
            || !Guid.TryParseExact(rawUserId, "D", out var parsedUserId)
            || !properties.Items.TryGetValue(
                ExternalOAuthStateProperties.SessionId,
                out var rawSessionId)
            || !Guid.TryParseExact(rawSessionId, "D", out var parsedSessionId))
        {
            return false;
        }

        userId = new UserId(parsedUserId);
        sessionId = new SessionId(parsedSessionId);
        return true;
    }

    private static IResult CallbackFailure(
        HttpContext http,
        ILogger logger,
        ExternalProvider provider,
        string code)
    {
        var safeCode = code switch
        {
            ApiProblemCodes.ExternalAuthFailed => code,
            ApiProblemCodes.ExternalEmailRequired => code,
            ApiProblemCodes.ExternalEmailUnverified => code,
            ApiProblemCodes.ExternalIdentityConflict => code,
            ApiProblemCodes.ExternalEmailConflict => code,
            ApiProblemCodes.OAuthFlowContextChanged => code,
            _ => ApiProblemCodes.ExternalAuthFailed
        };
        ExternalAuthSecurityEvents.Write(
            logger,
            "callback",
            provider.Value,
            safeCode,
            CorrelationIdMiddleware.GetTraceId(http),
            userId: null);
        return Results.Redirect($"{ErrorPath}{safeCode}");
    }

    private static string MapNormalizerFailure(AccountFailure failure) =>
        failure switch
        {
            AccountFailure.EmailRequired =>
                ApiProblemCodes.ExternalEmailRequired,
            AccountFailure.EmailUnverified =>
                ApiProblemCodes.ExternalEmailUnverified,
            AccountFailure.IdentityConflict =>
                ApiProblemCodes.ExternalIdentityConflict,
            _ => ApiProblemCodes.ExternalAuthFailed
        };

    private static string MapReconciliationFailure(AccountFailure failure) =>
        failure switch
        {
            AccountFailure.SessionRequired =>
                ApiProblemCodes.OAuthFlowContextChanged,
            AccountFailure.IdentityConflict =>
                ApiProblemCodes.ExternalIdentityConflict,
            AccountFailure.EmailConflict =>
                ApiProblemCodes.ExternalEmailConflict,
            AccountFailure.ConcurrencyConflict =>
                ApiProblemCodes.ExternalIdentityConflict,
            _ => ApiProblemCodes.ExternalAuthFailed
        };

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";
}

internal static partial class ExternalAuthSecurityEvents
{
    [LoggerMessage(
        EventId = 3110,
        Level = LogLevel.Information,
        Message =
            "External OAuth {Operation} for {Provider} finished with {Outcome}; CorrelationId={CorrelationId}; UserId={UserId}")]
    internal static partial void Write(
        ILogger logger,
        string operation,
        string provider,
        string outcome,
        string correlationId,
        Guid? userId);
}
