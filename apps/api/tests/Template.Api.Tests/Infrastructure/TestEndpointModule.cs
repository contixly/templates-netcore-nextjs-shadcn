using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.ApiKeys;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;

namespace Template.Api.Tests.Infrastructure;

internal sealed class TestEndpointModule : IEndpointModule
{
    internal const string ForbiddenPolicy = "Test.Forbidden";

    public void MapEndpoints(EndpointRouteContext context)
    {
        context.Root.MapGet("/api/testing/forbidden", () => Results.Ok())
            .RequireAuthorization(ForbiddenPolicy)
            .ExcludeFromDescription();

        context.Root.MapGet("/api/testing/fault", ThrowFault)
            .ExcludeFromDescription();

        context.Root.MapGet(
                "/api/testing/fault/by-key/{organizationKey}",
                (string organizationKey) => ThrowFault())
            .ExcludeFromDescription();

        context.Root.MapGet("/api/testing/bad-request", ThrowBadRequest)
            .ExcludeFromDescription();

        context.Root.MapGet(
                "/api/testing/nested-validation",
                () => Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Address.PostalCode"] = ["Postal code is required."],
                    ["address.PostalCode"] = ["Postal code has an invalid format."],
                    ["ContactInfo.EmailAddress"] = ["Email address is invalid."]
                }))
            .ExcludeFromDescription();

        context.Root.MapGet(
                "/api/testing/csrf",
                (IAntiforgery antiforgery, HttpContext httpContext) =>
                {
                    var tokens = antiforgery.GetAndStoreTokens(httpContext);
                    return Results.Ok(new { requestToken = tokens.RequestToken });
                })
            .AllowAnonymous()
            .ExcludeFromDescription();

        context.Root.MapPost(
                "/api/testing/csrf",
                () => Results.Ok(new { accepted = true }))
            .AllowAnonymous()
            .RequireApiAntiforgery()
            .ExcludeFromDescription();

        context.Root.MapGet(
                "/api/local-auth/testing",
                () => Results.Ok(new { enabled = true }))
            .AllowAnonymous()
            .WithLocalOnly()
            .ExcludeFromDescription();

        context.Root.MapPost(
                "/api/local-auth/testing-rate",
                () => Results.Ok(new { accepted = true }))
            .AllowAnonymous()
            .RequireApiAntiforgery()
            .RequireRateLimiting(AuthRateLimitPolicies.LocalAutomationCreate)
            .WithLocalOnly()
            .ExcludeFromDescription();

        context.Root.MapPost(
                "/api/testing/non-local-session",
                async (
                    UserManager<ApplicationUser> users,
                    IBrowserSessionGateway sessions,
                    TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    var now = timeProvider.GetUtcNow();
                    var row = new ApplicationUser
                    {
                        Id = Guid.CreateVersion7(now),
                        UserName = "person@example.test",
                        Email = "person@example.test",
                        DisplayName = "Non Local User",
                        EmailConfirmed = true,
                        IsLocalAutomation = false,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    var created = await users.CreateAsync(row);
                    if (!created.Succeeded)
                    {
                        throw new InvalidOperationException(
                            "Could not create test user.");
                    }

                    await sessions.SignInAsync(
                        new AuthUser(
                            new UserId(row.Id),
                            row.DisplayName,
                            row.Email,
                            row.EmailConfirmed,
                            row.ImageUrl,
                            row.IsLocalAutomation),
                        BrowserAuthenticationMethods.Local,
                        cancellationToken);
                    return Results.Ok();
                })
            .AllowAnonymous()
            .ExcludeFromDescription();

        context.VersionedMixedApi.MapGet(
                "/testing/consumer",
                (ClaimsPrincipal principal) => Results.Ok(new
                {
                    authenticationType = principal.Identity?.AuthenticationType,
                    claims = principal.Claims
                        .OrderBy(claim => claim.Type, StringComparer.Ordinal)
                        .ThenBy(claim => claim.Value, StringComparer.Ordinal)
                        .Select(claim => new { claim.Type, claim.Value })
                }))
            .RequireApiKeyScopes(ApiKeyScopes.BasicRead)
            .ExcludeFromDescription();

        context.VersionedMixedApi.MapGet(
                "/testing/consumer/organization-read",
                (ClaimsPrincipal principal) => Results.Ok(new
                {
                    authenticationType = principal.Identity?.AuthenticationType
                }))
            .RequireApiKeyScopes(ApiKeyScopes.OrganizationRead)
            .ExcludeFromDescription();

        context.Root.MapGet(
                "/api/testing/api-key-principal/{scenario}",
                async (
                    string scenario,
                    IAuthorizationService authorization) =>
                {
                    var principal = InjectedApiKeyPrincipal(scenario);
                    var result = await authorization.AuthorizeAsync(
                        principal,
                        resource: null,
                        [new ApiKeyScopeRequirement([ApiKeyScopes.BasicRead])]);
                    return Results.Ok(new
                    {
                        authorized = result.Succeeded,
                        readable = ApiKeyPrincipalReader.TryRead(
                            principal,
                            out _)
                    });
                })
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static ClaimsPrincipal InjectedApiKeyPrincipal(string scenario)
    {
        var identity = ValidApiKeyIdentity();
        switch (scenario)
        {
            case "valid":
                return new(identity);
            case "duplicate-identity":
                return new([identity, ValidApiKeyIdentity()]);
            case "additional-identity":
                return new([
                    identity,
                    new ClaimsIdentity(
                        [new Claim("test", "unrelated")],
                        "Test.Unrelated")
                ]);
            case "unknown-claim":
                identity.AddClaim(new Claim("urn:template:claim:unknown", "unsafe"));
                break;
            case "duplicate-claim":
                identity.AddClaim(new Claim(
                    ApiKeyClaimTypes.Id,
                    "0198a7ac-d0f8-7832-b711-211f56c57701"));
                break;
            case "mixed-owner":
                identity.AddClaim(new Claim(
                    ApiKeyClaimTypes.OrganizationId,
                    "0198a7ac-d0f8-7832-b711-211f56c57703"));
                break;
            case "invalid-scope":
                identity.AddClaim(new Claim(
                    ApiKeyClaimTypes.Scope,
                    "basic:write"));
                break;
            default:
                throw new BadHttpRequestException("Unknown test principal scenario.");
        }

        return new(identity);
    }

    private static ClaimsIdentity ValidApiKeyIdentity() => new(
        [
            new Claim(
                ApiKeyClaimTypes.Id,
                "0198a7ac-d0f8-7832-b711-211f56c57701"),
            new Claim(ApiKeyClaimTypes.Start, "user_abcdefghijk"),
            new Claim(ApiKeyClaimTypes.OwnerKind, "user"),
            new Claim(
                ApiKeyClaimTypes.UserId,
                "0198a7ac-d0f8-7832-b711-211f56c57702"),
            new Claim(ApiKeyClaimTypes.Scope, ApiKeyScopes.BasicRead)
        ],
        ApiKeyAuthenticationDefaults.SchemeName);

    private static IResult ThrowFault() =>
        throw new InvalidOperationException("sensitive-database-message");

    private static IResult ThrowBadRequest() =>
        throw new BadHttpRequestException("test malformed request", StatusCodes.Status400BadRequest);
}
