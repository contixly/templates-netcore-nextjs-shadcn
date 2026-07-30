using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Authentication;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Authentication;

public sealed class PostgresTicketStore(
    IDataProtectionProvider dataProtectionProvider,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
    : ITicketStore
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Template.Infrastructure.Authentication.PostgresTicketStore.v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken) =>
        StoreAsync(ticket, RequiredHttpContext(), cancellationToken);

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await StoreCoreAsync(ticket, key, httpContext, cancellationToken);
        return key;
    }

    private async Task StoreCoreAsync(
        AuthenticationTicket ticket,
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc.");
        var sessionId = ParseRequiredGuid(
            ticket.Principal,
            BrowserSessionClaimTypes.SessionId);
        var userId = ParseRequiredGuid(ticket.Principal, ClaimTypes.NameIdentifier);
        var authenticationMethod = NormalizeAuthenticationMethodClaim(
            ticket.Principal);
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (userAgent.Length > 512)
        {
            userAgent = userAgent[..512];
        }

        var db = GetDb(httpContext);
        db.Sessions.Add(new AuthSessionEntity
        {
            Id = sessionId,
            UserId = userId,
            TicketKeyHash = HashKey(key),
            ProtectedTicket = Protect(ticket),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt.ToUniversalTime(),
            AuthenticationMethod = authenticationMethod,
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = userAgent.Length == 0 ? null : userAgent
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, RequiredHttpContext(), cancellationToken);

    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var db = GetDb(httpContext);
        var hash = HashKey(key);
        var sessionId = ParseRequiredGuid(
            ticket.Principal,
            BrowserSessionClaimTypes.SessionId);
        var userId = ParseRequiredGuid(ticket.Principal, ClaimTypes.NameIdentifier);
        var authenticationMethod = NormalizeAuthenticationMethodClaim(
            ticket.Principal);
        var protectedTicket = Protect(ticket);
        var updatedAt = timeProvider.GetUtcNow();
        var expiresAt = (ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc."))
            .ToUniversalTime();
        await db.Sessions
            .Where(session =>
                session.TicketKeyHash.SequenceEqual(hash) &&
                session.Id == sessionId &&
                session.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        session => session.ProtectedTicket,
                        protectedTicket)
                    .SetProperty(session => session.UpdatedAt, updatedAt)
                    .SetProperty(session => session.ExpiresAt, expiresAt)
                    .SetProperty(
                        session => session.AuthenticationMethod,
                        authenticationMethod),
                cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken) =>
        RetrieveAsync(key, RequiredHttpContext(), cancellationToken);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var db = GetDb(httpContext);
        var hash = HashKey(key);
        var row = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (row.ExpiresAt <= now)
        {
            await db.Sessions
                .Where(session =>
                    session.TicketKeyHash.SequenceEqual(hash) &&
                    session.ExpiresAt <= now)
                .ExecuteDeleteAsync(cancellationToken);
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }

        AuthenticationTicket? ticket;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(
                _protector.Unprotect(row.ProtectedTicket));
        }
        catch (CryptographicException)
        {
            await DeleteIfUnchangedAsync(db, row, cancellationToken);
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }
        catch (IOException)
        {
            await DeleteIfUnchangedAsync(db, row, cancellationToken);
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }
        catch (ArgumentException)
        {
            await DeleteIfUnchangedAsync(db, row, cancellationToken);
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }

        if (ticket is null || !IsExpectedTicket(ticket, row))
        {
            await DeleteIfUnchangedAsync(db, row, cancellationToken);
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }

        var authenticationMethod = NormalizeAuthenticationMethodClaim(
            ticket.Principal);
        if (!string.Equals(
                authenticationMethod,
                row.AuthenticationMethod,
                StringComparison.Ordinal))
        {
            await DeleteIfUnchangedAsync(db, row, cancellationToken);
            BrowserSessionCookieInvalidation.Request(httpContext);
            return null;
        }

        return ticket;
    }

    public Task RemoveAsync(string key) =>
        RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        RemoveAsync(key, RequiredHttpContext(), cancellationToken);

    public async Task RemoveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var db = GetDb(httpContext);
        var hash = HashKey(key);
        await db.Sessions
            .Where(session => session.TicketKeyHash.SequenceEqual(hash))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private HttpContext RequiredHttpContext() =>
        httpContextAccessor.HttpContext ??
        throw new InvalidOperationException(
            "PostgresTicketStore requires the .NET 10 HttpContext overload.");

    private static TemplateDbContext GetDb(HttpContext context) =>
        context.RequestServices.GetRequiredService<TemplateDbContext>();

    private static byte[] HashKey(string key) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static Task<int> DeleteIfUnchangedAsync(
        TemplateDbContext db,
        AuthSessionEntity row,
        CancellationToken cancellationToken) =>
        db.Sessions
            .Where(session =>
                session.Id == row.Id &&
                session.ProtectedTicket.SequenceEqual(row.ProtectedTicket))
            .ExecuteDeleteAsync(cancellationToken);

    private byte[] Protect(AuthenticationTicket ticket) =>
        _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static bool IsExpectedTicket(
        AuthenticationTicket ticket,
        AuthSessionEntity row) =>
        IsExpectedScheme(ticket.AuthenticationScheme) &&
        HasSingleMatchingGuidClaim(
            ticket.Principal,
            BrowserSessionClaimTypes.SessionId,
            row.Id) &&
        HasSingleMatchingGuidClaim(
            ticket.Principal,
            ClaimTypes.NameIdentifier,
            row.UserId);

    private static bool IsExpectedScheme(string scheme) =>
        scheme is
            BrowserSessionAuthenticationDefaults.PrimaryScheme or
            BrowserSessionAuthenticationDefaults.IssuerScheme;

    private static bool HasSingleMatchingGuidClaim(
        ClaimsPrincipal principal,
        string claimType,
        Guid expected)
    {
        var claims = principal.FindAll(claimType).ToArray();
        return claims.Length == 1 &&
            Guid.TryParse(claims[0].Value, out var actual) &&
            actual == expected;
    }

    private static Guid ParseRequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Required claim '{claimType}' is missing.");
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

    private static string NormalizeAuthenticationMethodClaim(
        ClaimsPrincipal principal)
    {
        var authenticationMethod = ReadAuthenticationMethod(principal);
        var identity = principal.Identity as ClaimsIdentity ??
            throw new InvalidOperationException(
                "A persistent ticket requires a ClaimsIdentity.");
        foreach (var claim in principal
                     .FindAll(BrowserSessionClaimTypes.AuthenticationMethod)
                     .ToArray())
        {
            claim.Subject?.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(
            BrowserSessionClaimTypes.AuthenticationMethod,
            authenticationMethod));
        return authenticationMethod;
    }
}
