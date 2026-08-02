using Microsoft.AspNetCore.Mvc;
using Template.Api.Authentication;
using Template.Api.Contracts;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Features.Auth;
using Template.Api.OpenApi;
using Template.Application.ApiKeys;
using Template.Application.Authentication.Ports;
using Template.Application.Organizations;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Api.Features.ApiKeys;

internal sealed class ApiKeyEndpointModule : IEndpointModule
{
    public void MapEndpoints(EndpointRouteContext context)
    {
        MapOwnerRoutes(context.VersionedApi.MapGroup("/account/api-keys"), organization: false);
        MapOwnerRoutes(context.VersionedApi.MapGroup("/organizations/{organizationId}/api-keys"), organization: true);
    }

    private static void MapOwnerRoutes(RouteGroupBuilder group, bool organization)
    {
        var suffix = organization ? "Organization" : "Personal";
        group.MapGet("", ListAsync)
            .WithName($"List{suffix}ApiKeys")
            .Produces<ApiResponse<ApiKeyPageResponse>>()
            .ProducesValidationProblem()
            .ProducesProtectedApiProblems();
        group.MapPost("", CreateAsync)
            .WithName($"Create{suffix}ApiKey")
            .AcceptsManuallyReadJson<CreateApiKeyRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<ApiKeySecretResponse>>(StatusCodes.Status201Created)
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict, OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();
        group.MapPatch("/{apiKeyId}", UpdateAsync)
            .WithName($"Update{suffix}ApiKey")
            .AcceptsManuallyReadJson<UpdateApiKeyRequest>(isOptional: false)
            .RequireApiAntiforgery()
            .Produces<ApiResponse<ApiKeyResponse>>()
            .ProducesBadRequestVariants()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict, OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();
        group.MapDelete("/{apiKeyId}", RevokeAsync)
            .WithName($"Revoke{suffix}ApiKey")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<ApiKeyRevocationResponse>>()
            .ProducesBadRequestProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict, OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();
        group.MapPost("/{apiKeyId}/rotate", RotateAsync)
            .WithName($"Rotate{suffix}ApiKey")
            .RequireApiAntiforgery()
            .Produces<ApiResponse<ApiKeySecretResponse>>()
            .ProducesBadRequestProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict, OpenApiDefaults.ProblemContentType)
            .ProducesProtectedApiProblems();
    }

    private static async Task<IResult> ListAsync(
        string? organizationId,
        string? cursor,
        string? limit,
        ApiKeyManagementService apiKeys,
        OrganizationService organizations,
        IBrowserSessionGateway sessions,
        ILogger<ApiKeyEndpointModule> logger,
        TimeProvider timeProvider,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var actor = await ApiKeyEndpointBoundary.RequiredActorAsync(sessions, http.User, cancellationToken);
        var owner = Owner(actor, organizationId);
        const string operation = "list";
        try
        {
            await AuthorizeOwnerAsync(actor, owner, organizations, cancellationToken);
            var result = await apiKeys.ListAsync(new(
                actor,
                owner.Kind,
                owner.OrganizationId,
                ApiKeyEndpointBoundary.Cursor(http, cursor),
                ApiKeyEndpointBoundary.Limit(http, limit)), cancellationToken);
            var page = RequireSuccess(result);
            Audit(logger, operation, "succeeded", actor, owner, apiKeyId: null);
            var now = timeProvider.GetUtcNow();
            return Results.Ok(new ApiResponse<ApiKeyPageResponse>(new(
                page.Items.Select(item => Map(item, now)).ToArray(),
                page.NextCursor)));
        }
        catch (Exception exception)
        {
            AuditFailure(logger, operation, actor, owner, apiKeyId: null, exception);
            throw;
        }
    }

    private static async Task<IResult> CreateAsync(
        string? organizationId,
        ApiJsonRequestReader reader,
        ApiKeyManagementService apiKeys,
        OrganizationService organizations,
        IBrowserSessionGateway sessions,
        ILogger<ApiKeyEndpointModule> logger,
        TimeProvider timeProvider,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var actor = await ApiKeyEndpointBoundary.RequiredActorAsync(sessions, http.User, cancellationToken);
        var owner = Owner(actor, organizationId);
        const string operation = "create";
        try
        {
            await AuthorizeOwnerAsync(actor, owner, organizations, cancellationToken);
            var request = await reader.ReadAsync<CreateApiKeyRequest>(http, null, cancellationToken);
            ApiKeyEndpointBoundary.ValidateCreate(request);
            var result = await apiKeys.CreateAsync(new(
                actor,
                owner.Kind,
                owner.OrganizationId,
                request.Name,
                request.PresetIds,
                request.ExpiresIn,
                request.RateLimitEnabled!.Value,
                request.RateLimitMax!.Value,
                request.RateLimitWindow), cancellationToken);
            var secret = RequireSuccess(result);
            Audit(logger, operation, "succeeded", actor, owner, secret.ApiKey.Id.Value);
            var path = owner.Kind == ApiKeyOwnerKind.User
                ? $"/api/v1/account/api-keys/{secret.ApiKey.Id.Value:D}"
                : $"/api/v1/organizations/{owner.OrganizationId!.Value.Value:D}/api-keys/{secret.ApiKey.Id.Value:D}";
            return Results.Created(path, new ApiResponse<ApiKeySecretResponse>(
                Map(secret, timeProvider.GetUtcNow())));
        }
        catch (Exception exception)
        {
            AuditFailure(logger, operation, actor, owner, apiKeyId: null, exception);
            throw;
        }
    }

    private static async Task<IResult> UpdateAsync(
        string? organizationId,
        string apiKeyId,
        ApiJsonRequestReader reader,
        ApiKeyManagementService apiKeys,
        OrganizationService organizations,
        IBrowserSessionGateway sessions,
        ILogger<ApiKeyEndpointModule> logger,
        TimeProvider timeProvider,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var actor = await ApiKeyEndpointBoundary.RequiredActorAsync(sessions, http.User, cancellationToken);
        var owner = Owner(actor, organizationId);
        ApiKeyId? trustedId = null;
        const string operation = "update";
        try
        {
            trustedId = ApiKeyEndpointBoundary.ApiKeyId(apiKeyId);
            await AuthorizeOwnerAsync(actor, owner, organizations, cancellationToken);
            var request = await reader.ReadAsync<UpdateApiKeyRequest>(http, null, cancellationToken);
            var result = await apiKeys.UpdateAsync(new(
                actor, owner.Kind, owner.OrganizationId, trustedId.Value,
                request.Name, request.PresetIds, request.ExpiresIn, request.Enabled,
                request.RateLimitEnabled, request.RateLimitMax, request.RateLimitWindow), cancellationToken);
            var key = RequireSuccess(result);
            Audit(logger, operation, "succeeded", actor, owner, trustedId.Value.Value);
            return Results.Ok(new ApiResponse<ApiKeyResponse>(Map(key, timeProvider.GetUtcNow())));
        }
        catch (Exception exception)
        {
            AuditFailure(logger, operation, actor, owner, trustedId?.Value, exception);
            throw;
        }
    }

    private static async Task<IResult> RevokeAsync(
        string? organizationId,
        string apiKeyId,
        ApiKeyManagementService apiKeys,
        OrganizationService organizations,
        IBrowserSessionGateway sessions,
        ILogger<ApiKeyEndpointModule> logger,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var actor = await ApiKeyEndpointBoundary.RequiredActorAsync(sessions, http.User, cancellationToken);
        var owner = Owner(actor, organizationId);
        ApiKeyId? trustedId = null;
        const string operation = "revoke";
        try
        {
            trustedId = ApiKeyEndpointBoundary.ApiKeyId(apiKeyId);
            await AuthorizeOwnerAsync(actor, owner, organizations, cancellationToken);
            ApiKeyEndpointBoundary.RequireEmptyBody(http);
            var result = await apiKeys.RevokeAsync(new(
                actor, owner.Kind, owner.OrganizationId, trustedId.Value), cancellationToken);
            var revocation = RequireSuccess(result);
            Audit(logger, operation, "succeeded", actor, owner, trustedId.Value.Value);
            return Results.Ok(new ApiResponse<ApiKeyRevocationResponse>(
                new(revocation.Id.Value, revocation.RevokedAt)));
        }
        catch (Exception exception)
        {
            AuditFailure(logger, operation, actor, owner, trustedId?.Value, exception);
            throw;
        }
    }

    private static async Task<IResult> RotateAsync(
        string? organizationId,
        string apiKeyId,
        ApiKeyManagementService apiKeys,
        OrganizationService organizations,
        IBrowserSessionGateway sessions,
        ILogger<ApiKeyEndpointModule> logger,
        TimeProvider timeProvider,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var actor = await ApiKeyEndpointBoundary.RequiredActorAsync(sessions, http.User, cancellationToken);
        var owner = Owner(actor, organizationId);
        ApiKeyId? trustedId = null;
        const string operation = "rotate";
        try
        {
            trustedId = ApiKeyEndpointBoundary.ApiKeyId(apiKeyId);
            await AuthorizeOwnerAsync(actor, owner, organizations, cancellationToken);
            ApiKeyEndpointBoundary.RequireEmptyBody(http);
            var result = await apiKeys.RotateAsync(new(
                actor, owner.Kind, owner.OrganizationId, trustedId.Value), cancellationToken);
            var secret = RequireSuccess(result);
            Audit(logger, operation, "succeeded", actor, owner, trustedId.Value.Value);
            return Results.Ok(new ApiResponse<ApiKeySecretResponse>(Map(secret, timeProvider.GetUtcNow())));
        }
        catch (Exception exception)
        {
            AuditFailure(logger, operation, actor, owner, trustedId?.Value, exception);
            throw;
        }
    }

    private static ApiKeyOwner Owner(UserId actor, string? organizationId) =>
        organizationId is null
            ? new(ApiKeyOwnerKind.User, actor, null)
            : new(ApiKeyOwnerKind.Organization, null, ApiKeyEndpointBoundary.OrganizationId(organizationId));

    private static async Task AuthorizeOwnerAsync(
        UserId actor,
        ApiKeyOwner owner,
        OrganizationService organizations,
        CancellationToken cancellationToken)
    {
        if (owner.Kind == ApiKeyOwnerKind.User)
        {
            return;
        }

        var result = await organizations.GetByKeyAsync(
            actor,
            owner.OrganizationId!.Value.Value.ToString("D"),
            cancellationToken);
        if (!result.Succeeded)
        {
            throw result.Failure switch
            {
                OrganizationFailure.NotFound or OrganizationFailure.PermissionDenied =>
                    new ApiProblemException(
                        StatusCodes.Status404NotFound,
                        ApiProblemCodes.ApiKeyNotFound),
                OrganizationFailure.ConcurrencyConflict =>
                    new ApiProblemException(
                        StatusCodes.Status409Conflict,
                        ApiProblemCodes.ConcurrencyConflict),
                _ => new InvalidOperationException(
                    "Unexpected organization authorization failure for API key management.")
            };
        }

        var organization = result.Value ?? throw new InvalidOperationException(
            "Successful organization authorization requires a value.");
        if (!organization.Capabilities.CanManageApiKeys)
        {
            throw new ApiProblemException(
                StatusCodes.Status403Forbidden,
                ApiProblemCodes.ApiKeyPermissionDenied);
        }
    }

    private static T RequireSuccess<T>(ApiKeyOperationResult<T> result)
        where T : class
    {
        if (result.Succeeded)
        {
            return result.Value ?? throw new InvalidOperationException("Successful API key result requires a value.");
        }
        var failure = result.Failure ?? throw new InvalidOperationException("Failed API key result requires a failure.");
        Exception problem = failure switch
        {
            ApiKeyFailure.InvalidName => ApiKeyEndpointBoundary.Validation("name", "A valid API key name is required."),
            ApiKeyFailure.InvalidPreset => ApiKeyEndpointBoundary.Validation("presetIds", "Every API key preset must be valid."),
            ApiKeyFailure.InvalidExpiration => ApiKeyEndpointBoundary.Validation("expiresIn", "A valid API key expiration is required."),
            ApiKeyFailure.InvalidRateLimit => ApiKeyEndpointBoundary.Validation("rateLimit", "A valid API key rate limit is required."),
            ApiKeyFailure.InvalidCursor => ApiKeyEndpointBoundary.Validation("cursor", "The supplied pagination cursor is invalid."),
            ApiKeyFailure.PermissionDenied => new ApiProblemException(
                StatusCodes.Status403Forbidden, ApiProblemCodes.ApiKeyPermissionDenied),
            ApiKeyFailure.NotFound => new ApiProblemException(
                StatusCodes.Status404NotFound, ApiProblemCodes.ApiKeyNotFound),
            ApiKeyFailure.Unchanged => new ApiProblemException(
                StatusCodes.Status409Conflict, ApiProblemCodes.ApiKeyUpdateUnchanged),
            ApiKeyFailure.ConcurrencyConflict => new ApiProblemException(
                StatusCodes.Status409Conflict, ApiProblemCodes.ConcurrencyConflict),
            _ => throw new InvalidOperationException("Unexpected API key management failure.")
        };
        throw problem;
    }

    private static ApiKeyResponse Map(ApiKeySummary value, DateTimeOffset now) => new(
        value.Id.Value,
        OwnerKind(value.Owner),
        OwnerId(value.Owner),
        value.Name,
        value.Start,
        Status(value, now),
        value.Enabled,
        value.Scopes,
        value.RateLimitEnabled,
        value.RateLimitMax,
        RateWindow(value.RateLimitWindow),
        value.RequestCount,
        value.WindowStartedAt,
        value.LastRequestAt,
        value.ExpiresAt,
        value.RotatedAt,
        value.CreatedAt,
        value.UpdatedAt);

    private static ApiKeySecretResponse Map(ApiKeySecret value, DateTimeOffset now)
    {
        var key = Map(value.ApiKey, now);
        return new(
            key.Id, key.OwnerKind, key.OwnerId, key.Name, key.Start, key.Status,
            key.Enabled, key.Scopes, key.RateLimitEnabled, key.RateLimitMax,
            key.RateLimitWindow, key.RequestCount, key.WindowStartedAt,
            key.LastRequestAt, key.ExpiresAt, key.RotatedAt, key.CreatedAt,
            key.UpdatedAt, value.Credential);
    }

    private static string Status(ApiKeySummary value, DateTimeOffset now) =>
        !value.Enabled ? "disabled" : value.ExpiresAt <= now ? "expired" : "active";

    private static string RateWindow(TimeSpan value) => value switch
    {
        var window when window == TimeSpan.FromMinutes(1) => "1m",
        var window when window == TimeSpan.FromHours(1) => "1h",
        var window when window == TimeSpan.FromDays(1) => "1d",
        _ => throw new InvalidOperationException("Persisted API key rate window is invalid.")
    };

    private static string OwnerKind(ApiKeyOwner owner) =>
        owner.Kind == ApiKeyOwnerKind.User ? "user" : "organization";

    private static Guid OwnerId(ApiKeyOwner owner) =>
        owner.Kind == ApiKeyOwnerKind.User
            ? owner.UserId!.Value.Value
            : owner.OrganizationId!.Value.Value;

    private static void Audit(
        ILogger logger,
        string operation,
        string outcome,
        UserId actor,
        ApiKeyOwner owner,
        Guid? apiKeyId) =>
        ApiKeySecurityEvents.Write(
            logger, operation, outcome, actor.Value, OwnerKind(owner), OwnerId(owner), apiKeyId);

    private static void AuditFailure(
        ILogger logger,
        string operation,
        UserId actor,
        ApiKeyOwner owner,
        Guid? apiKeyId,
        Exception exception)
    {
        var outcome = exception switch
        {
            ApiProblemException problem => problem.Code,
            ApiValidationException => ApiProblemCodes.ValidationFailed,
            _ => "unexpected_failure"
        };
        Audit(logger, operation, outcome, actor, owner, apiKeyId);
    }
}
