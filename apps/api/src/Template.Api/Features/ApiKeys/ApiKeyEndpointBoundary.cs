using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Template.Api.Errors;
using Template.Application.Authentication.Ports;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Api.Features.ApiKeys;

internal static class ApiKeyEndpointBoundary
{
    internal const int DefaultPageLimit = 50;

    internal static async Task<UserId> RequiredActorAsync(
        IBrowserSessionGateway sessions,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var claimedValue = SingleClaim(principal, ClaimTypes.NameIdentifier);
        if (!Guid.TryParseExact(claimedValue, "D", out var claimedId) ||
            claimedId == Guid.Empty)
        {
            throw Problem(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized);
        }

        var current = await sessions.GetCurrentAsync(cancellationToken);
        if (current is null || current.User.Id.Value != claimedId)
        {
            throw Problem(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized);
        }

        return current.User.Id;
    }

    internal static OrganizationId OrganizationId(string value)
    {
        if (!TryCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation("organizationId", "A valid organization ID is required.");
        }

        return new OrganizationId(id);
    }

    internal static ApiKeyId ApiKeyId(string value)
    {
        if (!TryCanonicalUuid(value, out var id) || id == Guid.Empty)
        {
            throw Validation("apiKeyId", "A valid API key ID is required.");
        }

        return new ApiKeyId(id);
    }

    internal static int Limit(HttpContext http, string? boundValue)
    {
        var value = SingleQueryValue(http, "limit", boundValue);
        if (value is null)
        {
            return DefaultPageLimit;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            || limit is < 1 or > 100)
        {
            throw Validation("limit", "The field limit must be between 1 and 100.");
        }

        return limit;
    }

    internal static string? Cursor(HttpContext http, string? boundValue) =>
        SingleQueryValue(http, "cursor", boundValue);

    internal static void ValidateCreate(CreateApiKeyRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Name is null)
        {
            errors["name"] = ["The field name is required."];
        }
        if (request.PresetIds is null)
        {
            errors["presetIds"] = ["The field presetIds is required."];
        }
        if (request.ExpiresIn is null)
        {
            errors["expiresIn"] = ["The field expiresIn is required."];
        }
        if (request.RateLimitEnabled is null)
        {
            errors["rateLimitEnabled"] = ["The field rateLimitEnabled is required."];
        }
        if (request.RateLimitMax is null)
        {
            errors["rateLimitMax"] = ["The field rateLimitMax is required."];
        }
        if (request.RateLimitWindow is null)
        {
            errors["rateLimitWindow"] = ["The field rateLimitWindow is required."];
        }
        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }
    }

    internal static void RequireEmptyBody(HttpContext http)
    {
        if (http.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true ||
            http.Request.ContentLength is > 0 ||
            http.Request.Headers.TransferEncoding.Count > 0)
        {
            throw Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.InvalidRequest);
        }
    }

    internal static ApiValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static string? SingleQueryValue(
        HttpContext http,
        string name,
        string? boundValue)
    {
        if (!http.Request.Query.TryGetValue(name, out var values))
        {
            return null;
        }
        if (values.Count != 1 || !string.Equals(values[0], boundValue, StringComparison.Ordinal))
        {
            throw Validation(name, $"The field {name} must be supplied at most once.");
        }
        return values[0];
    }

    private static string? SingleClaim(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }

    private static bool TryCanonicalUuid(string? value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id) &&
        string.Equals(value, id.ToString("D"), StringComparison.OrdinalIgnoreCase);

    private static ApiProblemException Problem(int status, string code) => new(status, code);
}
