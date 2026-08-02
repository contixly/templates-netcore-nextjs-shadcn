using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Template.Api.Errors;
using Template.Api.Features.ApiKeys;
using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;

namespace Template.Api.Authentication;

internal sealed class ApiKeyAuthenticationHandler :
    AuthenticationHandler<AuthenticationSchemeOptions>
{
    private static readonly object FailureItemKey = new();
    private readonly ApiKeyAuthenticationService _authentication;
    private readonly IProblemDetailsService _problemDetails;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyAuthenticationService authentication,
        IProblemDetailsService problemDetails)
        : base(options, logger, encoder)
    {
        _authentication = authentication;
        _problemDetails = problemDetails;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Response.Headers.CacheControl = "no-store";
        if (!Request.Headers.TryGetValue(
                ApiKeyAuthenticationDefaults.HeaderName,
                out var values) ||
            values.Count == 0 ||
            values.Count == 1 && string.IsNullOrWhiteSpace(values[0]))
        {
            SetFailure(ApiKeyAuthenticationFailure.Missing);
            return AuthenticateResult.NoResult();
        }

        if (values.Count != 1)
        {
            SetFailure(ApiKeyAuthenticationFailure.Invalid);
            return AuthenticateResult.Fail("The API key header is invalid.");
        }

        var result = await _authentication.AuthenticateAsync(
            values[0]!,
            Context.RequestAborted);
        if (result.Outcome == ApiKeyAuthenticationOutcome.RateLimited)
        {
            var retryAfter = result.RetryAfter.GetValueOrDefault();
            SetFailure(new(
                ApiKeyAuthenticationFailureKind.RateLimited,
                checked((int)Math.Ceiling(retryAfter.TotalSeconds))));
            return AuthenticateResult.Fail("The API key rate limit was exceeded.");
        }

        if (result.Outcome != ApiKeyAuthenticationOutcome.Succeeded ||
            result.Principal is null ||
            !TryCreatePrincipal(result.Principal, out var claimsPrincipal))
        {
            SetFailure(ApiKeyAuthenticationFailure.Invalid);
            return AuthenticateResult.Fail("The API key is invalid.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(
            claimsPrincipal,
            ApiKeyAuthenticationDefaults.SchemeName));
    }

    protected override Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        var failure = Context.Items.TryGetValue(FailureItemKey, out var value) &&
                      value is ApiKeyAuthenticationFailure stored
            ? stored
            : InferFailure();
        ApiKeySecurityEvents.WriteMachine(
            Logger,
            MachineOperation(),
            failure.Kind switch
            {
                ApiKeyAuthenticationFailureKind.Missing => "missing",
                ApiKeyAuthenticationFailureKind.Invalid => "invalid",
                ApiKeyAuthenticationFailureKind.RateLimited => "rate_limited",
                _ => throw new ArgumentOutOfRangeException()
            },
            ownerKind: null,
            ownerId: null,
            apiKeyId: null);
        return failure.Kind == ApiKeyAuthenticationFailureKind.RateLimited
            ? WriteProblemAsync(
                StatusCodes.Status429TooManyRequests,
                ApiProblemCodes.ApiKeyRateLimited,
                failure.RetryAfterSeconds)
            : failure.Kind == ApiKeyAuthenticationFailureKind.Missing
                ? WriteProblemAsync(
                    StatusCodes.Status401Unauthorized,
                    ApiProblemCodes.ApiKeyMissing)
                : WriteProblemAsync(
                    StatusCodes.Status401Unauthorized,
                    ApiProblemCodes.ApiKeyInvalid);
    }

    protected override Task HandleForbiddenAsync(
        AuthenticationProperties properties)
    {
        if (ApiKeyPrincipalReader.TryRead(Context.User, out var principal))
        {
            ApiKeySecurityEvents.WriteMachine(
                Logger,
                MachineOperation(),
                "permission_denied",
                principal.Owner.Kind == ApiKeyOwnerKind.User
                    ? "user"
                    : "organization",
                principal.Owner.UserId?.Value ??
                principal.Owner.OrganizationId?.Value,
                principal.Id.Value);
        }
        else
        {
            ApiKeySecurityEvents.WriteMachine(
                Logger,
                MachineOperation(),
                "permission_denied",
                ownerKind: null,
                ownerId: null,
                apiKeyId: null);
        }

        return WriteProblemAsync(
            StatusCodes.Status403Forbidden,
            ApiProblemCodes.ApiKeyPermissionDenied);
    }

    private void SetFailure(ApiKeyAuthenticationFailure failure) =>
        Context.Items[FailureItemKey] = failure;

    private ApiKeyAuthenticationFailure InferFailure()
    {
        if (!Request.Headers.TryGetValue(
                ApiKeyAuthenticationDefaults.HeaderName,
                out var values) ||
            values.Count == 0 ||
            values.Count == 1 && string.IsNullOrWhiteSpace(values[0]))
        {
            return ApiKeyAuthenticationFailure.Missing;
        }

        return ApiKeyAuthenticationFailure.Invalid;
    }

    private string MachineOperation() =>
        Context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()
            ?.EndpointName switch
        {
            "GetApiKeyPrincipal" => "me",
            "GetOrganizations" => "organization_list",
            "GetMachineOrganization" => "organization_get",
            "GetOrganizationMembers" => "organization_members_list",
            _ => "unknown"
        };

    private async Task WriteProblemAsync(
        int status,
        string code,
        int? retryAfterSeconds = null)
    {
        Response.StatusCode = status;
        Response.Headers.CacheControl = "no-store";
        if (retryAfterSeconds is not null)
        {
            Response.Headers.RetryAfter = retryAfterSeconds.Value.ToString(
                CultureInfo.InvariantCulture);
        }

        var details = new ProblemDetails { Status = status };
        details.Extensions["code"] = code;
        await _problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = details
        });
    }

    private static bool TryCreatePrincipal(
        ApiKeyPrincipal principal,
        out ClaimsPrincipal claimsPrincipal)
    {
        claimsPrincipal = new();
        if (!ApiKeyPrincipalReader.TryCanonicalScopes(
                principal.Scopes,
                out var scopes))
        {
            return false;
        }

        var claims = new List<Claim>
        {
            new(ApiKeyClaimTypes.Id, principal.Id.Value.ToString("D")),
            new(ApiKeyClaimTypes.Start, principal.Start),
            new(
                ApiKeyClaimTypes.OwnerKind,
                principal.Owner.Kind == ApiKeyOwnerKind.User
                    ? "user"
                    : "organization")
        };
        switch (principal.Owner.Kind)
        {
            case ApiKeyOwnerKind.User
                when principal.Owner.UserId is not null &&
                     principal.Owner.OrganizationId is null:
                claims.Add(new(
                    ApiKeyClaimTypes.UserId,
                    principal.Owner.UserId.Value.Value.ToString("D")));
                break;
            case ApiKeyOwnerKind.Organization
                when principal.Owner.OrganizationId is not null &&
                     principal.Owner.UserId is null:
                claims.Add(new(
                    ApiKeyClaimTypes.OrganizationId,
                    principal.Owner.OrganizationId.Value.Value.ToString("D")));
                break;
            default:
                return false;
        }

        claims.AddRange(scopes.Select(scope =>
            new Claim(ApiKeyClaimTypes.Scope, scope)));
        claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            ApiKeyAuthenticationDefaults.SchemeName));
        return true;
    }

    private enum ApiKeyAuthenticationFailureKind
    {
        Missing,
        Invalid,
        RateLimited
    }

    private sealed record ApiKeyAuthenticationFailure(
        ApiKeyAuthenticationFailureKind Kind,
        int? RetryAfterSeconds = null)
    {
        internal static ApiKeyAuthenticationFailure Missing { get; } =
            new(ApiKeyAuthenticationFailureKind.Missing);
        internal static ApiKeyAuthenticationFailure Invalid { get; } =
            new(ApiKeyAuthenticationFailureKind.Invalid);
    }
}
