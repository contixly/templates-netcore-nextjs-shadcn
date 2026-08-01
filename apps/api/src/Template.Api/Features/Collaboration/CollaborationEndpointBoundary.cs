using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Template.Api.Errors;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Api.Features.Collaboration;

internal static class CollaborationEndpointBoundary
{
    internal const int DefaultPageLimit = 50;
    private const int MinimumPageLimit = 1;
    private const int MaximumPageLimit = 100;
    private const int MaximumCandidateQueryLength = 100;

    internal static async Task<CollaborationActorContext> RequiredActorAsync(
        IBrowserSessionGateway sessions,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var claimedUserId = Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? new UserId(userId)
            : throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        var current = await sessions.GetCurrentAsync(cancellationToken)
            ?? throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        if (current.User.Id != claimedUserId)
        {
            throw new ApiProblemException(
                StatusCodes.Status401Unauthorized,
                ApiProblemCodes.Unauthorized);
        }

        return new CollaborationActorContext(
            current.User.Id,
            current.Session.Id);
    }

    internal static async Task<T> AuditAsync<T>(
        Func<Task<T>> execute,
        string operation,
        CollaborationActorContext actor,
        ILogger logger,
        string? organizationId = null,
        string? teamId = null,
        string? targetUserId = null)
    {
        try
        {
            return await execute();
        }
        catch (ApiValidationException)
        {
            Write(
                logger,
                operation,
                ApiProblemCodes.ValidationFailed,
                actor,
                SafeOpaqueId(organizationId),
                SafeOpaqueId(teamId),
                SafeOpaqueId(targetUserId));
            throw;
        }
        catch (ApiProblemException problem)
            when (problem.Code == ApiProblemCodes.InvalidRequest)
        {
            Write(
                logger,
                operation,
                problem.Code,
                actor,
                SafeOpaqueId(organizationId),
                SafeOpaqueId(teamId),
                SafeOpaqueId(targetUserId));
            throw;
        }
    }

    internal static OrganizationId OrganizationId(string value)
    {
        if (!TryParseCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation(
                "organizationId",
                "A valid organization ID is required.");
        }

        return new OrganizationId(id);
    }

    internal static TeamId TeamId(string value)
    {
        if (!TryParseCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation("teamId", "A valid team ID is required.");
        }

        return new TeamId(id);
    }

    internal static UserId UserId(string? value, string field = "userId")
    {
        if (!TryParseCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation(field, "A valid user ID is required.");
        }

        return new UserId(id);
    }

    internal static int Limit(HttpContext http, string? boundValue)
    {
        var value = SingleQueryValue(http, "limit", boundValue);
        if (value is null)
        {
            return DefaultPageLimit;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var limit)
            || limit is < MinimumPageLimit or > MaximumPageLimit)
        {
            throw Validation(
                "limit",
                $"The field limit must be between {MinimumPageLimit} and {MaximumPageLimit}.");
        }

        return limit;
    }

    internal static string? Cursor(HttpContext http, string? boundValue) =>
        SingleQueryValue(http, "cursor", boundValue);

    internal static string? CandidateQuery(
        HttpContext http,
        string? boundValue)
    {
        var value = SingleQueryValue(http, "q", boundValue)?.Trim();
        if (value is { Length: > MaximumCandidateQueryLength })
        {
            throw Validation(
                "q",
                $"The field q must be at most {MaximumCandidateQueryLength} characters.");
        }

        return value;
    }

    internal static string TeamName(string? value)
    {
        if (Template.Domain.Collaboration.TeamName.TryCreate(
                value,
                out var name))
        {
            return name.Value;
        }

        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is < 1 or
            > Template.Domain.Collaboration.TeamName.MaximumLength)
        {
            throw Validation(
                "name",
                "A team name of at most 50 characters is required.");
        }

        throw Validation(
            "name",
            "The team name contains an unsupported character.");
    }

    internal static void RequireEmptyBody(HttpContext http)
    {
        if (http.Request.ContentLength is > 0 ||
            http.Request.Headers.TransferEncoding.Count > 0)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                ApiProblemCodes.InvalidRequest);
        }
    }

    internal static void Write(
        ILogger logger,
        string operation,
        string outcome,
        CollaborationActorContext actor,
        Guid? organizationId,
        Guid? teamId,
        Guid? targetUserId,
        int? resultCount = null) =>
        CollaborationSecurityEvents.Write(
            logger,
            operation,
            outcome,
            actor.UserId.Value,
            actor.SessionId.Value,
            organizationId,
            teamId,
            targetUserId,
            resultCount);

    internal static void NoStore(HttpContext http) =>
        http.Response.Headers.CacheControl = "no-store";

    private static string? SingleQueryValue(
        HttpContext http,
        string name,
        string? boundValue)
    {
        if (!http.Request.Query.TryGetValue(name, out StringValues values))
        {
            return null;
        }

        if (values.Count != 1)
        {
            throw Validation(name, $"The field {name} must be supplied once.");
        }

        return values[0] ?? boundValue;
    }

    private static Guid? SafeOpaqueId(string? value) =>
        TryParseCanonicalUuid(value, out var id) && id != Guid.Empty
            ? id
            : null;

    private static bool TryParseCanonicalUuid(string? value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id) &&
        string.Equals(
            value,
            id.ToString("D"),
            StringComparison.OrdinalIgnoreCase);

    private static ApiValidationException Validation(
        string field,
        string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

internal sealed record CollaborationActorContext(
    UserId UserId,
    SessionId SessionId);
