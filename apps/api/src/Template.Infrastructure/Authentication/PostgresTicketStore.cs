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
    private static readonly object SessionReplacementRequested = new();
    private static readonly object SignedOutTicketKey = new();
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
        httpContext.Items.Remove(SessionReplacementRequested);
        httpContext.Items.Remove(SignedOutTicketKey);
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
        var protectedTicket = Protect(ticket);
        var updatedAt = timeProvider.GetUtcNow();
        var expiresAt = (ticket.Properties.ExpiresUtc ??
            throw new InvalidOperationException("A persistent ticket requires ExpiresUtc."))
            .ToUniversalTime();
        var updated = await db.Sessions
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
                    .SetProperty(session => session.ExpiresAt, expiresAt),
                cancellationToken);
        if (updated == 0 &&
            httpContext.Items.TryGetValue(SignedOutTicketKey, out var signedOutKey) &&
            string.Equals(signedOutKey as string, key, StringComparison.Ordinal))
        {
            await StoreCoreAsync(ticket, key, httpContext, cancellationToken);
            httpContext.Items.Remove(SignedOutTicketKey);
        }
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
            return null;
        }

        try
        {
            var ticket = TicketSerializer.Default.Deserialize(
                _protector.Unprotect(row.ProtectedTicket));
            if (ticket is null)
            {
                await DeleteIfUnchangedAsync(db, row, cancellationToken);
            }

            return ticket;
        }
        catch (CryptographicException)
        {
            await DeleteIfUnchangedAsync(db, row, cancellationToken);
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
        await db.Sessions
            .Where(session => session.TicketKeyHash.SequenceEqual(hash))
            .ExecuteDeleteAsync(cancellationToken);
        if (httpContext.Items.Remove(SessionReplacementRequested))
        {
            httpContext.Items[SignedOutTicketKey] = key;
        }
    }

    private HttpContext RequiredHttpContext() =>
        httpContextAccessor.HttpContext ??
        throw new InvalidOperationException(
            "PostgresTicketStore requires the .NET 10 HttpContext overload.");

    private static AuthDbContext GetDb(HttpContext context) =>
        context.RequestServices.GetRequiredService<AuthDbContext>();

    private static byte[] HashKey(string key) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(key));

    internal static void BeginSessionReplacement(HttpContext context) =>
        context.Items[SessionReplacementRequested] = true;

    private static Task<int> DeleteIfUnchangedAsync(
        AuthDbContext db,
        AuthSessionEntity row,
        CancellationToken cancellationToken) =>
        db.Sessions
            .Where(session =>
                session.Id == row.Id &&
                session.ProtectedTicket.SequenceEqual(row.ProtectedTicket))
            .ExecuteDeleteAsync(cancellationToken);

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
