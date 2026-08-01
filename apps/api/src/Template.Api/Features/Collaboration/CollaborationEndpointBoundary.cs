using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Template.Api.Errors;
using Template.Application.Authentication.Ports;
using Template.Application.Collaboration;
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
        var claimedUserIdValue = SingleClaim(
            principal,
            ClaimTypes.NameIdentifier);
        var claimedUserId = Guid.TryParseExact(
            claimedUserIdValue,
            "D",
            out var userId) && userId != Guid.Empty &&
            string.Equals(
                claimedUserIdValue,
                userId.ToString("D"),
                StringComparison.Ordinal)
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
            current.Session.Id,
            current.User.Email.Trim().ToLowerInvariant(),
            current.User.EmailVerified);
    }

    internal static async Task<IResult> AuditAsync(
        Func<CollaborationAuditContext, Task<IResult>> execute,
        string operation,
        CollaborationActorContext actor,
        ILogger logger,
        string? organizationId = null,
        string? teamId = null,
        string? targetUserId = null)
    {
        var audit = new CollaborationAuditContext(
            SafeOpaqueId(organizationId),
            SafeOpaqueId(teamId),
            SafeOpaqueId(targetUserId));
        try
        {
            var result = await execute(audit);
            Write(
                logger,
                operation,
                "succeeded",
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.TargetUserId,
                audit.ResultCount);
            return result;
        }
        catch (ApiValidationException)
        {
            Write(
                logger,
                operation,
                ApiProblemCodes.ValidationFailed,
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.TargetUserId,
                audit.ResultCount);
            throw;
        }
        catch (ApiProblemException problem)
        {
            Write(
                logger,
                operation,
                problem.Code,
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.TargetUserId,
                audit.ResultCount);
            throw;
        }
        catch (Exception)
        {
            Write(
                logger,
                operation,
                "unexpected_failure",
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.TargetUserId,
                audit.ResultCount);
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

    internal static InvitationId InvitationId(string value)
    {
        if (!TryParseCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation(
                "invitationId",
                "A valid invitation ID is required.");
        }

        return new InvitationId(id);
    }

    internal static TeamId? OptionalTeamId(string? value)
    {
        if (value is null)
        {
            return null;
        }

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

    internal static InvitationDisplayState? InvitationStatus(
        HttpContext http,
        string? boundValue)
    {
        var value = SingleQueryValue(http, "status", boundValue);
        if (value is null)
        {
            return null;
        }

        if (value == InvitationDisplayState.Pending.Value)
        {
            return InvitationDisplayState.Pending;
        }

        if (value == InvitationDisplayState.Accepted.Value)
        {
            return InvitationDisplayState.Accepted;
        }

        if (value == InvitationDisplayState.Rejected.Value)
        {
            return InvitationDisplayState.Rejected;
        }

        if (value == InvitationDisplayState.Canceled.Value)
        {
            return InvitationDisplayState.Canceled;
        }

        if (value == InvitationDisplayState.Expired.Value)
        {
            return InvitationDisplayState.Expired;
        }

        throw Validation("status", "The invitation status is not supported.");
    }

    internal static string InvitationEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Validation("email", "A valid email is required.");
        }

        var email = value.Trim();
        if (email.Length > 254 ||
            email.Any(char.IsControl) ||
            email.Any(character => !char.IsAscii(character)) ||
            !new EmailAddressAttribute().IsValid(email))
        {
            throw Validation(
                "email",
                "A valid email of at most 254 characters is required.");
        }

        return email.ToLowerInvariant();
    }

    internal static OrganizationRole InvitationRole(string? value)
    {
        if (!OrganizationRole.TryParse(value, out var role))
        {
            throw Validation("role", "A valid invitation role is required.");
        }

        return role;
    }

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
        if (http.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ||
            http.Request.ContentLength is > 0 ||
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

    internal static async Task<IResult> AuditInvitationAsync(
        Func<InvitationAuditContext, Task<IResult>> execute,
        string operation,
        CollaborationActorContext actor,
        ILogger logger,
        string? organizationId = null,
        string? teamId = null,
        string? invitationId = null)
    {
        var audit = new InvitationAuditContext(
            SafeOpaqueId(organizationId),
            SafeOpaqueId(teamId),
            SafeOpaqueId(invitationId));
        try
        {
            var result = await execute(audit);
            WriteInvitation(
                logger,
                operation,
                "succeeded",
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.InvitationId,
                audit.ResultCount);
            return result;
        }
        catch (ApiValidationException)
        {
            WriteInvitation(
                logger,
                operation,
                ApiProblemCodes.ValidationFailed,
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.InvitationId,
                audit.ResultCount);
            throw;
        }
        catch (ApiProblemException problem)
        {
            WriteInvitation(
                logger,
                operation,
                problem.Code,
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.InvitationId,
                audit.ResultCount);
            throw;
        }
        catch (Exception)
        {
            WriteInvitation(
                logger,
                operation,
                "unexpected_failure",
                actor,
                audit.OrganizationId,
                audit.TeamId,
                audit.InvitationId,
                audit.ResultCount);
            throw;
        }
    }

    internal static void WriteInvitation(
        ILogger logger,
        string operation,
        string outcome,
        CollaborationActorContext actor,
        Guid? organizationId,
        Guid? teamId,
        Guid? invitationId,
        int? resultCount = null) =>
        CollaborationSecurityEvents.WriteInvitation(
            logger,
            operation,
            outcome,
            actor.UserId.Value,
            actor.SessionId.Value,
            organizationId,
            teamId,
            invitationId,
            resultCount);

    internal static Guid? CanonicalOpaqueId(string? value) =>
        SafeOpaqueId(value);

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

    private static string? SingleClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        var claims = principal.FindAll(claimType).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }

    private static ApiValidationException Validation(
        string field,
        string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

internal sealed record CollaborationActorContext(
    UserId UserId,
    SessionId SessionId,
    string NormalizedPrimaryEmail,
    bool IsEmailVerified)
{
    internal InvitationActor InvitationActor =>
        new(UserId, NormalizedPrimaryEmail, IsEmailVerified);
}

internal sealed class CollaborationAuditContext(
    Guid? organizationId,
    Guid? teamId,
    Guid? targetUserId)
{
    internal Guid? OrganizationId { get; private set; } = organizationId;

    internal Guid? TeamId { get; private set; } = teamId;

    internal Guid? TargetUserId { get; private set; } = targetUserId;

    internal int? ResultCount { get; private set; }

    internal void SetOrganizationId(OrganizationId value) =>
        OrganizationId = value.Value;

    internal void SetTeamId(TeamId value) => TeamId = value.Value;

    internal void SetTargetUserId(UserId value) => TargetUserId = value.Value;

    internal void SetResultCount(int value) => ResultCount = value;
}

internal sealed class InvitationAuditContext(
    Guid? organizationId,
    Guid? teamId,
    Guid? invitationId)
{
    internal Guid? OrganizationId { get; private set; } = organizationId;

    internal Guid? TeamId { get; private set; } = teamId;

    internal Guid? InvitationId { get; private set; } = invitationId;

    internal int? ResultCount { get; private set; }

    internal void SetOrganizationId(OrganizationId value) =>
        OrganizationId = value.Value;

    internal void SetTeamId(TeamId? value) => TeamId = value?.Value;

    internal void SetInvitationId(InvitationId value) =>
        InvitationId = value.Value;

    internal void SetResultCount(int value) => ResultCount = value;
}
