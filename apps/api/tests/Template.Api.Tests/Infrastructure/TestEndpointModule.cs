using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
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

        context.VersionedApi.MapGet("/testing/consumer", () => Results.Ok())
            .ExcludeFromDescription();
    }

    private static IResult ThrowFault() =>
        throw new InvalidOperationException("sensitive-database-message");

    private static IResult ThrowBadRequest() =>
        throw new BadHttpRequestException("test malformed request", StatusCodes.Status400BadRequest);
}
