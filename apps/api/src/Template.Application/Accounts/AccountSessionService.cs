using Template.Application.Accounts.Ports;
using Template.Domain.Authentication;

namespace Template.Application.Accounts;

public sealed class AccountSessionService(IAccountSessionStore sessions)
{
    public async Task<AccountOperationResult<CursorPage<AccountSession>>> ListAsync(
        UserId userId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Session page limit must be between 1 and 100.");
        }

        SessionCursor? decodedCursor = null;
        if (cursor is not null)
        {
            if (!SessionCursor.TryDecode(cursor, out var decoded))
            {
                return Failed<CursorPage<AccountSession>>(AccountFailure.InvalidCursor);
            }

            decodedCursor = decoded;
        }

        var page = await sessions.ListAsync(
            userId,
            decodedCursor,
            limit,
            cancellationToken);
        return Succeeded(page);
    }

    public async Task<AccountOperationResult<AccountSessionRevocation>> RevokeAsync(
        UserId userId,
        SessionId sessionId,
        SessionId currentSessionId,
        CancellationToken cancellationToken)
    {
        if (sessionId == currentSessionId)
        {
            return Failed<AccountSessionRevocation>(
                AccountFailure.CurrentSessionCannotBeRevoked);
        }

        var revoked = await sessions.RevokeAsync(
            userId,
            sessionId,
            cancellationToken);
        return revoked
            ? Succeeded(new AccountSessionRevocation(sessionId))
            : Failed<AccountSessionRevocation>(AccountFailure.SessionNotFound);
    }

    public Task<int> RevokeOthersAsync(
        UserId userId,
        SessionId currentSessionId,
        CancellationToken cancellationToken) =>
        sessions.RevokeOthersAsync(userId, currentSessionId, cancellationToken);

    private static AccountOperationResult<T> Succeeded<T>(T value)
        where T : class =>
        new(value, null);

    private static AccountOperationResult<T> Failed<T>(AccountFailure failure)
        where T : class =>
        new(null, failure);
}
