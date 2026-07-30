using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Authentication;

internal sealed class BrowserSessionGateway(
    IHttpContextAccessor httpContextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    UserManager<ApplicationUser> users,
    AuthDbContext db,
    TimeProvider timeProvider)
    : IBrowserSessionGateway
{
    internal static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    public async Task<AuthenticatedSession?> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var context = RequiredHttpContext();
        if (context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(
                context.User.FindFirstValue(BrowserSessionClaimTypes.SessionId),
                out var sessionId))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var value = await (
            from session in db.Sessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            where session.Id == sessionId && session.ExpiresAt > now
            select new { session, user })
            .SingleOrDefaultAsync(cancellationToken);
        return value is null
            ? null
            : new AuthenticatedSession(Map(value.user), Map(value.session));
    }

    public async Task<BrowserSession> SignInAsync(
        AuthUser user,
        string authenticationMethod,
        CancellationToken cancellationToken)
    {
        if (!BrowserAuthenticationMethods.IsAllowed(authenticationMethod))
        {
            throw new ArgumentOutOfRangeException(
                nameof(authenticationMethod),
                authenticationMethod,
                "Authentication method is not supported.");
        }

        var context = RequiredHttpContext();
        var applicationUser = await users.FindByIdAsync(user.Id.Value.ToString()) ??
            throw new InvalidOperationException("Identity user disappeared before sign-in.");
        var principal = await principalFactory.CreateAsync(applicationUser);
        var sessionId = SessionId.New();
        AddSessionClaims(principal, sessionId, authenticationMethod);
        var window = SessionWindow.Start(timeProvider.GetUtcNow(), SessionLifetime);

        BrowserSessionReplacement.Begin(context);
        await context.SignOutAsync(
            BrowserSessionAuthenticationDefaults.PrimaryScheme);
        await context.SignInAsync(
            BrowserSessionAuthenticationDefaults.IssuerScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                IssuedUtc = window.CreatedAt,
                ExpiresUtc = window.ExpiresAt
            });

        var stored = await db.Sessions.AsNoTracking().SingleAsync(
            row => row.Id == sessionId.Value,
            cancellationToken);
        return Map(stored);
    }

    public async Task<BrowserSession> RenewCurrentAsync(
        CancellationToken cancellationToken)
    {
        var context = RequiredHttpContext();
        var authentication = await context.AuthenticateAsync(
            BrowserSessionAuthenticationDefaults.PrimaryScheme);
        if (!authentication.Succeeded ||
            authentication.Principal is null ||
            authentication.Properties is null)
        {
            throw new InvalidOperationException(
                "A current browser session is required for renewal.");
        }

        var sessionId = ParseSingleSessionId(authentication.Principal);
        var authenticationMethod = ReadAuthenticationMethod(
            authentication.Principal);
        var userId = authentication.Principal.FindFirstValue(
            ClaimTypes.NameIdentifier);
        var applicationUser = userId is null
            ? null
            : await users.FindByIdAsync(userId);
        if (applicationUser is null)
        {
            throw new InvalidOperationException(
                "Identity user disappeared before session renewal.");
        }

        var now = timeProvider.GetUtcNow();
        var exists = await db.Sessions.AsNoTracking().AnyAsync(
            row =>
                row.Id == sessionId.Value &&
                row.UserId == applicationUser.Id &&
                row.ExpiresAt > now,
            cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException(
                "The current browser session is no longer available.");
        }

        var principal = await principalFactory.CreateAsync(applicationUser);
        AddSessionClaims(principal, sessionId, authenticationMethod);
        await context.SignInAsync(
            BrowserSessionAuthenticationDefaults.PrimaryScheme,
            principal,
            authentication.Properties);

        var stored = await db.Sessions.AsNoTracking().SingleAsync(
            row => row.Id == sessionId.Value,
            cancellationToken);
        return Map(stored);
    }

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        RequiredHttpContext().SignOutAsync(
            BrowserSessionAuthenticationDefaults.PrimaryScheme);

    private HttpContext RequiredHttpContext() =>
        httpContextAccessor.HttpContext ??
        throw new InvalidOperationException("A browser-session operation requires HttpContext.");

    private static AuthUser Map(ApplicationUser user) =>
        new(
            new UserId(user.Id),
            user.DisplayName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.ImageUrl,
            user.IsLocalAutomation);

    private static BrowserSession Map(AuthSessionEntity session) =>
        new(
            new SessionId(session.Id),
            session.CreatedAt,
            session.UpdatedAt,
            session.ExpiresAt,
            BrowserAuthenticationMethods.Project(session.AuthenticationMethod));

    private static void AddSessionClaims(
        ClaimsPrincipal principal,
        SessionId sessionId,
        string authenticationMethod)
    {
        var identity = principal.Identity as ClaimsIdentity ??
            throw new InvalidOperationException(
                "Identity principal has no ClaimsIdentity.");
        foreach (var claim in principal
                     .FindAll(BrowserSessionClaimTypes.SessionId)
                     .Concat(principal.FindAll(
                         BrowserSessionClaimTypes.AuthenticationMethod))
                     .ToArray())
        {
            claim.Subject?.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.SessionId,
            sessionId.Value.ToString()));
        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.AuthenticationMethod,
            authenticationMethod));
    }

    private static SessionId ParseSingleSessionId(ClaimsPrincipal principal)
    {
        var claims = principal
            .FindAll(BrowserSessionClaimTypes.SessionId)
            .ToArray();
        return claims.Length == 1 &&
            Guid.TryParse(claims[0].Value, out var value)
            ? new SessionId(value)
            : throw new InvalidOperationException(
                "The current browser session id is invalid.");
    }

    private static string ReadAuthenticationMethod(ClaimsPrincipal principal)
    {
        var claims = principal
            .FindAll(BrowserSessionClaimTypes.AuthenticationMethod)
            .ToArray();
        return claims.Length == 1
            ? BrowserAuthenticationMethods.Project(claims[0].Value)
            : BrowserAuthenticationMethods.Local;
    }
}
