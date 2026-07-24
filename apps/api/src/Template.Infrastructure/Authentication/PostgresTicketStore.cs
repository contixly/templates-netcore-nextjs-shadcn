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
        var now = timeProvider.GetUtcNow();
        var expiresAt = ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc.");
        var sessionId = ParseRequiredGuid(
            ticket.Principal,
            BrowserSessionClaimTypes.SessionId);
        var userId = ParseRequiredGuid(ticket.Principal, ClaimTypes.NameIdentifier);
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
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = userAgent.Length == 0 ? null : userAgent
        });
        await db.SaveChangesAsync(cancellationToken);
        return key;
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
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            return;
        }

        row.ProtectedTicket = Protect(ticket);
        row.UpdatedAt = timeProvider.GetUtcNow();
        row.ExpiresAt = (ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc."))
            .ToUniversalTime();
        await db.SaveChangesAsync(cancellationToken);
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
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            return null;
        }

        if (row.ExpiresAt <= timeProvider.GetUtcNow())
        {
            db.Sessions.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(
                _protector.Unprotect(row.ProtectedTicket));
        }
        catch (CryptographicException)
        {
            db.Sessions.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
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
        var row = await db.Sessions.SingleOrDefaultAsync(
            session => session.TicketKeyHash.SequenceEqual(hash),
            cancellationToken);
        if (row is null)
        {
            return;
        }

        db.Sessions.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private HttpContext RequiredHttpContext() =>
        httpContextAccessor.HttpContext ??
        throw new InvalidOperationException(
            "PostgresTicketStore requires the .NET 10 HttpContext overload.");

    private static AuthDbContext GetDb(HttpContext context) =>
        context.RequestServices.GetRequiredService<AuthDbContext>();

    private static byte[] HashKey(string key) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private byte[] Protect(AuthenticationTicket ticket) =>
        _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static Guid ParseRequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        var value = principal.FindFirstValue(claimType);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Required claim '{claimType}' is missing.");
    }
}
