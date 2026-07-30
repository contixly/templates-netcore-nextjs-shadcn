using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Features.Auth;
using Template.Api.OpenApi;
using Template.Application.Accounts;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using Template.Infrastructure.Authentication;

namespace Template.Api.Features.Account;

internal sealed class AccountEndpointModule : IEndpointModule
{
    private const int DefaultSessionLimit = 20;
    private const int MinimumSessionLimit = 1;
    private const int MaximumSessionLimit = 100;
    private const int MaximumUserAgentLength = 512;

    public void MapEndpoints(EndpointRouteContext context)
    {
        context.VersionedApi.MapGet(
                "/account",
                GetAccountAsync)
            .WithName("GetAccount")
            .Produces<ApiResponse<AccountResponse>>()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapPatch(
                "/account/profile",
                UpdateProfileAsync)
            .WithName("UpdateAccountProfile")
            .AcceptsManuallyReadJson<UpdateProfileRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AccountResponse>>()
            .ProducesBadRequestVariants()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapGet(
                "/account/connections",
                GetConnectionsAsync)
            .WithName("GetAccountConnections")
            .Produces<ApiResponse<AccountConnectionsResponse>>()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/account/connections/{provider}",
                DisconnectAsync)
            .WithName("DisconnectAccountProvider")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AccountDisconnectionResponse>>()
            .ProducesBadRequestProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapGet(
                "/account/sessions",
                GetSessionsAsync)
            .WithName("GetAccountSessions")
            .Produces<ApiResponse<AccountSessionsResponse>>()
            .ProducesValidationProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/account/sessions/others",
                RevokeOtherSessionsAsync)
            .WithName("RevokeOtherAccountSessions")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AccountSessionsRevocationResponse>>()
            .ProducesBadRequestProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/account/sessions/{sessionId:guid}",
                RevokeSessionAsync)
            .WithName("RevokeAccountSession")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AccountSessionRevocationResponse>>()
            .ProducesBadRequestProblem()
            .ProducesProtectedApiProblems();

        context.VersionedApi.MapDelete(
                "/account",
                DeleteAccountAsync)
            .WithName("DeleteAccount")
            .AcceptsManuallyReadJson<DeleteAccountRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<AccountDeletionResponse>>()
            .ProducesBadRequestVariants()
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> GetAccountAsync(
        AccountService accounts,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var account = await accounts.GetAsync(
            CurrentUserId(http.User),
            cancellationToken);
        return Results.Ok(new ApiResponse<AccountResponse>(
            MapRequiredAccount(account)));
    }

    private static async Task<IResult> UpdateProfileAsync(
        ApiJsonRequestReader reader,
        AccountService accounts,
        ILogger<AccountEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var request = await reader.ReadAsync<UpdateProfileRequest>(
            http,
            emptyBodyFactory: null,
            cancellationToken);
        var displayName = ValidateDisplayName(request.DisplayName);
        var result = await accounts.UpdateDisplayNameAsync(
            userId,
            displayName,
            cancellationToken);
        if (result.Failure is not null)
        {
            switch (result.Failure.Value)
            {
                case AccountFailure.SessionRequired:
                    AccountSecurityEvents.Write(
                        logger,
                        "profile_update",
                        ApiProblemCodes.Unauthorized,
                        userId.Value,
                        sessionId: null,
                        providerId: null);
                    throw new ApiProblemException(
                        StatusCodes.Status401Unauthorized,
                        ApiProblemCodes.Unauthorized);
                case AccountFailure.InvalidDisplayName:
                    throw new InvalidOperationException(
                        "The HTTP display-name validator and account service disagreed.");
                default:
                    throw new InvalidOperationException(
                        "Unexpected profile-update failure.");
            }
        }

        AccountSecurityEvents.Write(
            logger,
            "profile_update",
            "succeeded",
            userId.Value,
            sessionId: null,
            providerId: null);
        return Results.Ok(new ApiResponse<AccountResponse>(
            MapRequiredAccount(result.Value)));
    }

    private static async Task<IResult> GetConnectionsAsync(
        AccountService accounts,
        IExternalProviderCatalog providers,
        IBrowserSessionGateway browserSessions,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var current = await RequiredSessionAsync(
            browserSessions,
            cancellationToken);
        var connections = await accounts.ListConnectionsAsync(
            userId,
            providers.Known
                .Where(provider => provider.Configured)
                .Select(provider => provider.Provider)
                .ToArray(),
            cancellationToken);
        return Results.Ok(new ApiResponse<AccountConnectionsResponse>(
            new(MapConnections(
                connections,
                providers,
                current.Session.AuthenticationMethod))));
    }

    private static async Task<IResult> DisconnectAsync(
        string provider,
        AccountService accounts,
        IExternalProviderCatalog providers,
        IBrowserSessionGateway browserSessions,
        ILogger<AccountEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        if (!ExternalProvider.TryParse(provider, out var externalProvider))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.ExternalConnectionNotFound);
        }

        var current = await RequiredSessionAsync(
            browserSessions,
            cancellationToken);
        ExternalProvider? currentProvider =
            ExternalProvider.TryParse(
                current.Session.AuthenticationMethod,
                out var parsedCurrent)
                ? parsedCurrent
                : null;
        var result = await accounts.DisconnectAsync(
            userId,
            currentProvider,
            externalProvider,
            providers.Known
                .Where(candidate => candidate.Configured)
                .Select(candidate => candidate.Provider)
                .ToArray(),
            cancellationToken);
        if (result.Failure is not null)
        {
            AccountSecurityEvents.Write(
                logger,
                "provider_disconnect",
                MapFailureCode(result.Failure.Value),
                userId.Value,
                current.Session.Id.Value,
                externalProvider.Value);
            ThrowDisconnectFailure(result.Failure.Value);
        }

        AccountSecurityEvents.Write(
            logger,
            "provider_disconnect",
            "succeeded",
            userId.Value,
            current.Session.Id.Value,
            externalProvider.Value);
        return Results.Ok(new ApiResponse<AccountDisconnectionResponse>(
            new(result.Value!.Provider.Value)));
    }

    private static async Task<IResult> GetSessionsAsync(
        string? cursor,
        int? limit,
        AccountSessionService sessions,
        IBrowserSessionGateway browserSessions,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var pageLimit = limit ?? DefaultSessionLimit;
        if (pageLimit is < MinimumSessionLimit or > MaximumSessionLimit)
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["limit"] =
                    [
                        $"The field limit must be between {MinimumSessionLimit} and {MaximumSessionLimit}."
                    ]
                });
        }

        if (cursor is not null && !SessionCursor.TryDecode(cursor, out _))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidCursor);
        }

        var userId = CurrentUserId(http.User);
        var current = await RequiredSessionAsync(
            browserSessions,
            cancellationToken);
        var result = await sessions.ListAsync(
            userId,
            cursor,
            pageLimit,
            cancellationToken);
        if (result.Failure == AccountFailure.InvalidCursor)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidCursor);
        }

        var page = result.Value ??
            throw new InvalidOperationException("The session page is missing.");
        return Results.Ok(new ApiResponse<AccountSessionsResponse>(
            new(
                page.Items
                    .Select(session => MapSession(
                        session,
                        current.Session.Id))
                    .ToArray(),
                page.NextCursor)));
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        AccountSessionService sessions,
        IBrowserSessionGateway browserSessions,
        ILogger<AccountEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var current = await RequiredSessionAsync(
            browserSessions,
            cancellationToken);
        var requestedSessionId = new SessionId(sessionId);
        var result = await sessions.RevokeAsync(
            userId,
            requestedSessionId,
            current.Session.Id,
            cancellationToken);
        if (result.Failure is not null)
        {
            AccountSecurityEvents.Write(
                logger,
                "session_revoke",
                MapFailureCode(result.Failure.Value),
                userId.Value,
                sessionId,
                providerId: null);
            throw result.Failure switch
            {
                AccountFailure.SessionNotFound => new ApiProblemException(
                    StatusCodes.Status404NotFound,
                    ApiProblemCodes.AccountSessionNotFound),
                AccountFailure.CurrentSessionCannotBeRevoked =>
                    new ApiProblemException(
                        StatusCodes.Status409Conflict,
                        ApiProblemCodes.CurrentSessionCannotBeRevoked),
                _ => new InvalidOperationException(
                    "Unexpected session-revocation failure.")
            };
        }

        AccountSecurityEvents.Write(
            logger,
            "session_revoke",
            "succeeded",
            userId.Value,
            sessionId,
            providerId: null);
        return Results.Ok(new ApiResponse<AccountSessionRevocationResponse>(
            new(result.Value!.SessionId.Value)));
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(
        AccountSessionService sessions,
        IBrowserSessionGateway browserSessions,
        ILogger<AccountEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var current = await RequiredSessionAsync(
            browserSessions,
            cancellationToken);
        var revokedCount = await sessions.RevokeOthersAsync(
            userId,
            current.Session.Id,
            cancellationToken);
        AccountSecurityEvents.Write(
            logger,
            "sessions_revoke_others",
            "succeeded",
            userId.Value,
            current.Session.Id.Value,
            providerId: null);
        return Results.Ok(new ApiResponse<AccountSessionsRevocationResponse>(
            new(revokedCount)));
    }

    private static async Task<IResult> DeleteAccountAsync(
        ApiJsonRequestReader reader,
        AccountService accounts,
        IBrowserSessionGateway browserSessions,
        ILogger<AccountEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        NoStore(http);
        var userId = CurrentUserId(http.User);
        var request = await reader.ReadAsync<DeleteAccountRequest>(
            http,
            emptyBodyFactory: null,
            cancellationToken);
        var confirmationEmail = ValidateConfirmationEmail(
            request.ConfirmationEmail);
        var current = await RequiredSessionAsync(
            browserSessions,
            cancellationToken);
        var result = await accounts.DeleteAsync(
            userId,
            confirmationEmail,
            cancellationToken);
        if (result.Failure is not null)
        {
            AccountSecurityEvents.Write(
                logger,
                "account_delete",
                MapFailureCode(result.Failure.Value),
                userId.Value,
                current.Session.Id.Value,
                providerId: null);
            if (result.Failure == AccountFailure.ConfirmationMismatch)
            {
                throw ConfirmationValidationException();
            }

            if (result.Failure ==
                AccountFailure.OrganizationOwnershipTransferRequired)
            {
                throw new ApiProblemException(
                    StatusCodes.Status409Conflict,
                    ApiProblemCodes
                        .OrganizationOwnershipTransferRequired);
            }

            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        }

        await browserSessions.SignOutAsync(cancellationToken);
        AccountSecurityEvents.Write(
            logger,
            "account_delete",
            "succeeded",
            userId.Value,
            current.Session.Id.Value,
            providerId: null);
        return Results.Ok(new ApiResponse<AccountDeletionResponse>(
            new(Deleted: true)));
    }

    private static AccountResponse MapRequiredAccount(AccountSnapshot? account)
    {
        if (account is null)
        {
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        }

        return new AccountResponse(
            account.User.Id.Value,
            account.User.Name,
            account.PrimaryEmail.Value,
            ProjectHttpsImage(account.User.Image),
            account.CreatedAt,
            account.Emails
                .Select(email => new AccountEmailResponse(
                    email.Email.Value,
                    email.IsPrimary,
                    email.Providers
                        .Select(provider => provider.Value)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray()))
                .ToArray());
    }

    private static IReadOnlyList<AccountConnectionResponse> MapConnections(
        IReadOnlyList<AccountConnection> connections,
        IExternalProviderCatalog providers,
        string authenticationMethod)
    {
        var currentProvider = ExternalProvider.TryParse(
            authenticationMethod,
            out var parsed)
            ? parsed
            : null;
        var displayNames = providers.Known.ToDictionary(
            descriptor => descriptor.Provider,
            descriptor => descriptor.DisplayName);
        return connections
            .Select(connection =>
            {
                var connected = connection.Email is not null;
                var isCurrent = connected &&
                    connection.Provider == currentProvider;
                var configuredSurvivorCount = connections.Count(candidate =>
                    candidate.Provider != connection.Provider
                    && candidate.Configured
                    && candidate.Email is not null);
                var canDisconnect = connected &&
                    ExternalConnectionPolicy.CanDisconnect(
                        currentProvider,
                        connection.Provider,
                        configuredSurvivorCount);
                return new AccountConnectionResponse(
                    connection.Provider.Value,
                    displayNames.GetValueOrDefault(
                        connection.Provider,
                        connection.Provider.Value),
                    connection.Configured,
                    connected,
                    connection.Email?.Value,
                    connection.ConnectedAt,
                    connection.LastUsedAt,
                    isCurrent,
                    connection.Configured && !connected,
                    canDisconnect,
                    connected && !canDisconnect
                        ? ApiProblemCodes.ExternalConnectionRequired
                        : null);
            })
            .ToArray();
    }

    private static AccountSessionResponse MapSession(
        AccountSession session,
        SessionId currentSessionId) =>
        new(
            session.Id.Value,
            session.CreatedAt,
            session.LastSeenAt,
            session.ExpiresAt,
            session.Id == currentSessionId,
            BrowserAuthenticationMethods.Project(
                session.AuthenticationMethod),
            RedactIpAddress(session.IpAddress),
            session.UserAgent is { Length: > MaximumUserAgentLength } userAgent
                ? userAgent[..MaximumUserAgentLength]
                : session.UserAgent);

    internal static string? RedactIpAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            bytes[3] = 0;
            return $"{new IPAddress(bytes)}/24";
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Array.Clear(bytes, 8, 8);
            return $"{new IPAddress(bytes)}/64";
        }

        return null;
    }

    private static string ValidateDisplayName(string? value)
    {
        var normalized = value?.Trim();
        if (normalized is null
            || normalized.Length is < 2 or > 50
            || normalized.Any(char.IsControl))
        {
            throw new ApiValidationException(
                new Dictionary<string, string[]>
                {
                    ["displayName"] =
                    [
                        "Display name must contain 2 to 50 non-control characters after trimming."
                    ]
                });
        }

        return normalized;
    }

    private static string ValidateConfirmationEmail(string? value)
    {
        var normalized = value?.Trim();
        if (normalized is null
            || normalized.Length is 0 or > 254
            || !new EmailAddressAttribute().IsValid(normalized)
            || normalized.Any(char.IsControl))
        {
            throw ConfirmationValidationException();
        }

        return normalized;
    }

    private static ApiValidationException ConfirmationValidationException() =>
        new(
            new Dictionary<string, string[]>
            {
                ["confirmationEmail"] =
                [
                    "Confirmation email must exactly match the current primary email."
                ]
            });

    private static void ThrowDisconnectFailure(AccountFailure failure)
    {
        throw failure switch
        {
            AccountFailure.ConnectionNotFound => new ApiProblemException(
                StatusCodes.Status404NotFound,
                ApiProblemCodes.ExternalConnectionNotFound),
            AccountFailure.ConnectionRequired => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.ExternalConnectionRequired),
            AccountFailure.ConcurrencyConflict => new ApiProblemException(
                StatusCodes.Status409Conflict,
                ApiProblemCodes.ConcurrencyConflict),
            _ => new InvalidOperationException(
                "Unexpected provider-disconnection failure.")
        };
    }

    private static string MapFailureCode(AccountFailure failure) =>
        failure switch
        {
            AccountFailure.ConnectionNotFound =>
                ApiProblemCodes.ExternalConnectionNotFound,
            AccountFailure.ConnectionRequired =>
                ApiProblemCodes.ExternalConnectionRequired,
            AccountFailure.ConcurrencyConflict =>
                ApiProblemCodes.ConcurrencyConflict,
            AccountFailure.SessionNotFound =>
                ApiProblemCodes.AccountSessionNotFound,
            AccountFailure.CurrentSessionCannotBeRevoked =>
                ApiProblemCodes.CurrentSessionCannotBeRevoked,
            AccountFailure.ConfirmationMismatch =>
                ApiProblemCodes.ValidationFailed,
            AccountFailure.SessionRequired =>
                ApiProblemCodes.Unauthorized,
            AccountFailure.OrganizationOwnershipTransferRequired =>
                ApiProblemCodes.OrganizationOwnershipTransferRequired,
            _ => failure.ToString()
        };

    private static async Task<AuthenticatedSession> RequiredSessionAsync(
        IBrowserSessionGateway sessions,
        CancellationToken cancellationToken) =>
        await sessions.GetCurrentAsync(cancellationToken)
        ?? throw new ApiProblemException(
            StatusCodes.Status401Unauthorized,
            ApiProblemCodes.Unauthorized);

    private static UserId CurrentUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? new UserId(userId)
            : throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);

    private static string? ProjectHttpsImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var image)
        && image.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(image.Host)
            ? image.AbsoluteUri
            : null;

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";
}
