using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.OpenApi;
using Template.Application.Authentication;
using Template.Infrastructure.Authentication;

namespace Template.Api.Features.Auth;

internal sealed class AuthEndpointModule : IEndpointModule
{
    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedApi.MapGet(
                "/auth/capabilities",
                (
                    ILocalAutomationAuthAvailability availability,
                    IExternalProviderCatalog providers,
                    HttpContext http) =>
                {
                    NoStore(http);
                    return Results.Ok(new ApiResponse<AuthCapabilitiesResponse>(
                        new AuthCapabilitiesResponse(
                            availability.IsEnabled,
                            providers.Known
                                .Where(provider => provider.Configured)
                                .Select(provider => new AuthProviderResponse(
                                    provider.Provider.Value,
                                    provider.DisplayName))
                                .ToArray())));
                })
            .AllowAnonymous()
            .WithName("GetAuthCapabilities")
            .Produces<ApiResponse<AuthCapabilitiesResponse>>()
            .ProducesPublicApiProblems();

        context.VersionedApi.MapGet(
                "/auth/session",
                async (
                    BrowserAuthenticationService auth,
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    NoStore(http);
                    await BrowserSessionRenewal.RenewIfRequestedAsync(http);
                    return Results.Ok(new ApiResponse<AuthSessionResponse>(
                        Map(await auth.GetSessionAsync(cancellationToken))));
                })
            .AllowAnonymous()
            .WithName("GetAuthSession")
            .Produces<ApiResponse<AuthSessionResponse>>()
            .ProducesPublicApiProblems();

        context.VersionedApi.MapGet(
                "/auth/csrf",
                (IAntiforgery antiforgery, HttpContext http) =>
                {
                    NoStore(http);
                    var tokens = antiforgery.GetAndStoreTokens(http);
                    return Results.Ok(new ApiResponse<AuthCsrfResponse>(
                        new AuthCsrfResponse(
                            tokens.RequestToken ??
                            throw new InvalidOperationException(
                                "Antiforgery did not issue a request token."))));
                })
            .AllowAnonymous()
            .WithName("GetAuthCsrf")
            .Produces<ApiResponse<AuthCsrfResponse>>()
            .ProducesPublicApiProblems();

        context.VersionedApi.MapPost(
                "/auth/logout",
                async (
                    BrowserAuthenticationService auth,
                    ILogger<AuthEndpointModule> logger,
                    HttpContext http,
                    CancellationToken cancellationToken) =>
                {
                    NoStore(http);
                    var userId = CurrentUserId(http.User);
                    var result = await auth.LogoutAsync(cancellationToken);
                    if (!result.Succeeded)
                    {
                        AuthSecurityEvents.Write(
                            logger,
                            "logout",
                            "unauthorized",
                            userId,
                            sessionId: null);
                        throw new ApiProblemException(
                            StatusCodes.Status401Unauthorized,
                            ApiProblemCodes.Unauthorized);
                    }

                    AuthSecurityEvents.Write(
                        logger,
                        "logout",
                        "succeeded",
                        userId,
                        sessionId: null);
                    return Results.Ok(new ApiResponse<AuthSessionResponse>(
                        Map(result.Value!)));
                })
            .WithName("Logout")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AuthSessionResponse>>()
            .ProducesBadRequestProblem()
            .ProducesProtectedApiProblems();

        context.Root.MapPost(
                "/api/local-auth/scenario",
                CreateScenarioAsync)
            .AllowAnonymous()
            .WithName("CreateLocalAutomationScenario")
            .AcceptsManuallyReadJson<CreateLocalAutomationScenarioRequest>(
                isOptional: true)
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.LocalAutomationCreate)
            .WithLocalOnly()
            .Produces<ApiResponse<LocalAutomationScenarioResponse>>(
                StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .ProducesLocalCreateProblems();

        context.Root.MapPost(
                "/api/local-auth/sign-in",
                SignInAsync)
            .AllowAnonymous()
            .WithName("SignInLocalAutomation")
            .AcceptsManuallyReadJson<LocalAutomationSignInRequest>(
                isOptional: false)
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.LocalAutomationSignIn)
            .WithLocalOnly()
            .Produces<ApiResponse<AuthSessionResponse>>()
            .ProducesBadRequestVariants()
            .ProducesLocalSignInProblems();

        context.Root.MapDelete(
                "/api/local-auth/scenario",
                CleanupAsync)
            .RequireAuthorization(ApiPolicies.BrowserSession)
            .WithName("DeleteLocalAutomationScenario")
            .RequireApiAntiforgery()
            .WithLocalOnly()
            .Produces<ApiResponse<LocalAutomationCleanupResponse>>()
            .ProducesBadRequestProblem()
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> CreateScenarioAsync(
        ApiJsonRequestReader reader,
        LocalAutomationAuthService auth,
        ILogger<AuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var request = await reader.ReadAsync(
            http,
            () => new CreateLocalAutomationScenarioRequest(),
            cancellationToken);
        ValidateTrimmed(request.Name, "name", 2, 50);
        var email = ValidateAndTrimEmail(request.Email, required: false);
        var result = await auth.CreateScenarioAsync(
            new CreateLocalScenarioInput(
                request.Name?.Trim(),
                email,
                request.Password),
            cancellationToken);
        if (!result.Succeeded)
        {
            AuthSecurityEvents.Write(
                logger,
                "scenario_create",
                result.Failure!.Value.ToString(),
                userId: null,
                sessionId: null);
            ThrowCreateFailure(result.Failure!.Value);
        }

        var value = result.Value!;
        AuthSecurityEvents.Write(
            logger,
            "scenario_create",
            "succeeded",
            value.User.Id.Value,
            value.Session.Id.Value);
        return Results.Json(
            new ApiResponse<LocalAutomationScenarioResponse>(
                new(
                    Map(value.User),
                    value.Credentials.Email,
                    value.Credentials.Password,
                    value.CleanupUrl)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> SignInAsync(
        ApiJsonRequestReader reader,
        LocalAutomationAuthService auth,
        ILogger<AuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var request = await reader.ReadAsync<LocalAutomationSignInRequest>(
            http,
            emptyBodyFactory: null,
            cancellationToken);
        var email = ValidateAndTrimEmail(request.Email, required: true)!;
        var result = await auth.SignInAsync(
            new LocalCredentialInput(
                email,
                request.Password),
            cancellationToken);
        if (!result.Succeeded)
        {
            AuthSecurityEvents.Write(
                logger,
                "credential_sign_in",
                "invalid_credentials",
                userId: null,
                sessionId: null);
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.LocalAuthInvalidCredentials);
        }

        var value = result.Value!;
        AuthSecurityEvents.Write(
            logger,
            "credential_sign_in",
            "succeeded",
            value.User.Id.Value,
            value.Session.Id.Value);
        return Results.Ok(new ApiResponse<AuthSessionResponse>(
            Map(SessionState.From(value))));
    }

    private static async Task<IResult> CleanupAsync(
        LocalAutomationAuthService auth,
        ILogger<AuthEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var result = await auth.CleanupAsync(cancellationToken);
        if (!result.Succeeded)
        {
            AuthSecurityEvents.Write(
                logger,
                "scenario_cleanup",
                result.Failure!.Value.ToString(),
                userId,
                sessionId: null);
            throw result.Failure switch
            {
                AuthFailure.SessionRequired => new ApiProblemException(
                    StatusCodes.Status401Unauthorized,
                    ApiProblemCodes.Unauthorized),
                AuthFailure.LocalUserRequired => new ApiProblemException(
                    StatusCodes.Status403Forbidden,
                    ApiProblemCodes.LocalAuthUserRequired),
                AuthFailure.OrganizationOwnershipTransferRequired =>
                    new ApiProblemException(
                        StatusCodes.Status409Conflict,
                        ApiProblemCodes
                            .OrganizationOwnershipTransferRequired),
                AuthFailure.ConcurrencyConflict =>
                    new ApiProblemException(
                        StatusCodes.Status409Conflict,
                        ApiProblemCodes.ConcurrencyConflict),
                _ => new InvalidOperationException(
                    "Unexpected cleanup failure.")
            };
        }

        AuthSecurityEvents.Write(
            logger,
            "scenario_cleanup",
            "succeeded",
            userId,
            sessionId: null);
        return Results.Ok(new ApiResponse<LocalAutomationCleanupResponse>(
            new(result.Value!.DeletedOrganizations)));
    }

    private static void ThrowCreateFailure(AuthFailure failure)
    {
        if (failure == AuthFailure.InvalidLocalEmail)
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["email"] =
                    [
                        "Email must use the local-agent+...@local-agent.test namespace."
                    ]
                });
        }

        if (failure == AuthFailure.UserExists)
        {
            throw new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.LocalAuthUserExists);
        }

        throw new InvalidOperationException("Unexpected scenario creation failure.");
    }

    private static void ValidateTrimmed(
        string? value,
        string field,
        int minimum,
        int maximum)
    {
        if (value is null)
        {
            return;
        }

        var length = value.Trim().Length;
        if (length < minimum || length > maximum)
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    [field] =
                    [
                        $"The field {field} must be between {minimum} and {maximum} characters."
                    ]
                });
        }
    }

    private static string? ValidateAndTrimEmail(
        string? value,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (!required && value is null)
            {
                return null;
            }

            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["email"] = ["A valid email is required."]
                });
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 254 ||
            !new EmailAddressAttribute().IsValid(trimmed))
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["email"] = ["A valid email of at most 254 characters is required."]
                });
        }

        return trimmed;
    }

    private static AuthSessionResponse Map(SessionState state) =>
        new(
            state.Authenticated,
            state.User is null ? null : Map(state.User),
            state.Session is null
                ? null
                : new AuthSessionMetadataResponse(
                    state.Session.Id.Value,
                    state.Session.CreatedAt,
                    state.Session.UpdatedAt,
                    state.Session.ExpiresAt));

    private static AuthUserResponse Map(AuthUser user) =>
        new(
            user.Id.Value,
            user.Name,
            user.Email,
            user.EmailVerified,
            user.Image);

    private static Guid? CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var parsedUserId)
            ? parsedUserId
            : null;

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";
}
