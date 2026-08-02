using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Domain.Organizations;

namespace Template.Api.Authentication;

internal sealed record ApiKeyScopeRequirement(
    IReadOnlyList<string> Scopes) : IAuthorizationRequirement;

internal sealed class ApiKeyScopeAuthorizationHandler
    : AuthorizationHandler<ApiKeyScopeRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiKeyScopeRequirement requirement)
    {
        if (context.User.Identities.Any(identity => string.Equals(
                identity.AuthenticationType,
                ApiKeyAuthenticationDefaults.SchemeName,
                StringComparison.Ordinal)))
        {
            if (ApiKeyPrincipalReader.TryRead(
                    context.User,
                    out var principal) &&
                requirement.Scopes.All(scope => principal.Scopes.Contains(
                    scope,
                    StringComparer.Ordinal)))
            {
                context.Succeed(requirement);
            }

            return;
        }

        if (context.Resource is HttpContext http)
        {
            var browser = await http.AuthenticateAsync(
                ApiAuthenticationDefaults.SchemeName);
            if (browser.Succeeded && browser.Principal is not null)
            {
                context.Succeed(requirement);
            }
        }
    }
}

internal static class ApiKeyAuthorizationExtensions
{
    internal static TBuilder RequireApiKeyScopes<TBuilder>(
        this TBuilder builder,
        params string[] scopes)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scopes);
        if (!ApiKeyPrincipalReader.TryCanonicalScopes(scopes, out var canonical) ||
            canonical.Count != scopes.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException(
                "API key scope metadata must contain only known scopes.",
                nameof(scopes));
        }

        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new ApiKeyScopeRequirement(canonical))
            .Build();
        return builder.RequireAuthorization(policy);
    }
}

internal static class ApiKeyPrincipalReader
{
    private static readonly HashSet<string> KnownClaimTypes = new(
        [
            ApiKeyClaimTypes.Id,
            ApiKeyClaimTypes.Start,
            ApiKeyClaimTypes.OwnerKind,
            ApiKeyClaimTypes.UserId,
            ApiKeyClaimTypes.OrganizationId,
            ApiKeyClaimTypes.Scope
        ],
        StringComparer.Ordinal);

    private static readonly string[] CanonicalScopeOrder =
    [
        ApiKeyScopes.BasicRead,
        ApiKeyScopes.OrganizationRead,
        ApiKeyScopes.MemberRead,
        ApiKeyScopes.TeamRead,
        ApiKeyScopes.TeamMemberRead
    ];

    internal static bool TryRead(
        ClaimsPrincipal claims,
        out ApiKeyPrincipal principal)
    {
        principal = null!;
        var identities = claims.Identities.ToArray();
        if (identities.Length != 1 ||
            !identities[0].IsAuthenticated ||
            !string.Equals(
                identities[0].AuthenticationType,
                ApiKeyAuthenticationDefaults.SchemeName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var identity = identities[0];
        var allClaims = identity.Claims.ToArray();
        if (allClaims.Any(claim => !KnownClaimTypes.Contains(claim.Type)))
        {
            return false;
        }

        if (!TrySingle(identity, ApiKeyClaimTypes.Id, out var idValue) ||
            !TryCanonicalGuid(idValue, out var idGuid) ||
            !TrySingle(identity, ApiKeyClaimTypes.Start, out var start) ||
            !TrySingle(identity, ApiKeyClaimTypes.OwnerKind, out var ownerKind) ||
            !TryCanonicalScopes(
                identity.FindAll(ApiKeyClaimTypes.Scope)
                    .Select(claim => claim.Value),
                out var scopes))
        {
            return false;
        }

        ApiKeyOwner owner;
        if (string.Equals(ownerKind, "user", StringComparison.Ordinal) &&
            TrySingle(identity, ApiKeyClaimTypes.UserId, out var userIdValue) &&
            TryCanonicalGuid(userIdValue, out var userId) &&
            IsSafeStart(start, "user_") &&
            !identity.HasClaim(claim =>
                claim.Type == ApiKeyClaimTypes.OrganizationId))
        {
            owner = new(
                ApiKeyOwnerKind.User,
                new UserId(userId),
                null);
        }
        else if (string.Equals(
                     ownerKind,
                     "organization",
                     StringComparison.Ordinal) &&
                 TrySingle(
                     identity,
                     ApiKeyClaimTypes.OrganizationId,
                     out var organizationIdValue) &&
                 TryCanonicalGuid(organizationIdValue, out var organizationId) &&
                 IsSafeStart(start, "org_") &&
                 !identity.HasClaim(claim =>
                     claim.Type == ApiKeyClaimTypes.UserId))
        {
            owner = new(
                ApiKeyOwnerKind.Organization,
                null,
                new OrganizationId(organizationId));
        }
        else
        {
            return false;
        }

        principal = new(new ApiKeyId(idGuid), start, owner, scopes);
        return true;
    }

    internal static bool TryCanonicalScopes(
        IEnumerable<string> candidates,
        out IReadOnlyList<string> scopes)
    {
        var values = candidates.ToArray();
        if (values.Length == 0 ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
            values.Any(scope => !CanonicalScopeOrder.Contains(
                scope,
                StringComparer.Ordinal)))
        {
            scopes = [];
            return false;
        }

        var included = values.ToHashSet(StringComparer.Ordinal);
        scopes = CanonicalScopeOrder.Where(included.Contains).ToArray();
        return true;
    }

    private static bool TrySingle(
        ClaimsIdentity identity,
        string type,
        out string value)
    {
        var claims = identity.FindAll(type).ToArray();
        value = claims.Length == 1 ? claims[0].Value : string.Empty;
        return claims.Length == 1;
    }

    private static bool TryCanonicalGuid(string value, out Guid id) =>
        Guid.TryParseExact(value, "D", out id) &&
        string.Equals(value, id.ToString("D"), StringComparison.Ordinal);

    private static bool IsSafeStart(string value, string prefix) =>
        value.Length == 16 &&
        value.StartsWith(prefix, StringComparison.Ordinal) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
