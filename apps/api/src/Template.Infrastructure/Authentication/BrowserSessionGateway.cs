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
        CancellationToken cancellationToken)
    {
        var context = RequiredHttpContext();
        var applicationUser = await users.FindByIdAsync(user.Id.Value.ToString()) ??
            throw new InvalidOperationException("Identity user disappeared before sign-in.");
        var principal = await principalFactory.CreateAsync(applicationUser);
        var identity = principal.Identity as ClaimsIdentity ??
            throw new InvalidOperationException("Identity principal has no ClaimsIdentity.");
        var sessionId = SessionId.New();
        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.SessionId,
            sessionId.Value.ToString()));
        var window = SessionWindow.Start(timeProvider.GetUtcNow(), SessionLifetime);

        await context.SignInAsync(
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

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        RequiredHttpContext().SignOutAsync();

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
            session.ExpiresAt);
}
