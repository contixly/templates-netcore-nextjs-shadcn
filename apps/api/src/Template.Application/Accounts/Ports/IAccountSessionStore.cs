using Template.Domain.Authentication;

namespace Template.Application.Accounts.Ports;

public interface IAccountSessionStore
{
    Task<CursorPage<AccountSession>> ListAsync(
        UserId userId,
        SessionCursor? cursor,
        int limit,
        CancellationToken ct);

    Task<bool> RevokeAsync(
        UserId userId,
        SessionId sessionId,
        CancellationToken ct);

    Task<int> RevokeOthersAsync(
        UserId userId,
        SessionId current,
        CancellationToken ct);
}
