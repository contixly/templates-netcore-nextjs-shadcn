using Microsoft.EntityFrameworkCore;
using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Domain.Authentication;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Accounts;

internal sealed class EfAccountSessionStore(
    TemplateDbContext db,
    TimeProvider timeProvider)
    : IAccountSessionStore
{
    public async Task<CursorPage<AccountSession>> ListAsync(
        UserId userId,
        SessionCursor? cursor,
        int limit,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var query = db.Sessions.AsNoTracking()
            .Where(row =>
                row.UserId == userId.Value
                && row.ExpiresAt > now);
        if (cursor is { } value)
        {
            query = query.Where(row =>
                row.UpdatedAt < value.LastSeenAt
                || (row.UpdatedAt == value.LastSeenAt
                    && row.Id.CompareTo(value.Id.Value) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.UpdatedAt)
            .ThenByDescending(row => row.Id)
            .Take(limit + 1)
            .Select(row => new
            {
                row.Id,
                row.CreatedAt,
                row.UpdatedAt,
                row.ExpiresAt,
                row.AuthenticationMethod,
                row.IpAddress,
                row.UserAgent
            })
            .ToArrayAsync(ct);
        var hasMore = rows.Length > limit;
        var items = rows
            .Take(limit)
            .Select(row => new AccountSession(
                new SessionId(row.Id),
                row.CreatedAt,
                row.UpdatedAt,
                row.ExpiresAt,
                BrowserAuthenticationMethods.Project(row.AuthenticationMethod),
                row.IpAddress?.ToString(),
                row.UserAgent))
            .ToArray();
        var nextCursor = hasMore && items.Length > 0
            ? SessionCursor.Encode(new SessionCursor(
                items[^1].LastSeenAt,
                items[^1].Id))
            : null;
        return new CursorPage<AccountSession>(items, nextCursor);
    }

    public async Task<bool> RevokeAsync(
        UserId userId,
        SessionId sessionId,
        CancellationToken ct) =>
        await db.Sessions
            .Where(row =>
                row.UserId == userId.Value
                && row.Id == sessionId.Value)
            .ExecuteDeleteAsync(ct) == 1;

    public Task<int> RevokeOthersAsync(
        UserId userId,
        SessionId current,
        CancellationToken ct) =>
        db.Sessions
            .Where(row =>
                row.UserId == userId.Value
                && row.Id != current.Value)
            .ExecuteDeleteAsync(ct);
}
