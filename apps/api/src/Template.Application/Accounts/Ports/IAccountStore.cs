using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Accounts.Ports;

public interface IAccountStore
{
    Task<AccountSnapshot?> GetAsync(UserId userId, CancellationToken ct);

    Task<AccountSnapshot?> UpdateDisplayNameAsync(
        UserId userId,
        string displayName,
        CancellationToken ct);

    Task<IReadOnlyList<AccountConnection>> ListConnectionsAsync(
        UserId userId,
        CancellationToken ct);

    Task<DisconnectSnapshot?> GetDisconnectSnapshotAsync(
        UserId userId,
        ExternalProvider provider,
        CancellationToken ct);

    /// <summary>
    /// Atomically locks and rechecks all values in <paramref name="snapshot"/>,
    /// removes the external login, removes only an orphaned non-primary email,
    /// and commits all changes before completing. Implementations must preserve
    /// primary or still-vouched-for emails and roll back every change on failure.
    /// </summary>
    /// <exception cref="AccountConcurrencyException">
    /// The locked state no longer matches <paramref name="snapshot"/>.
    /// </exception>
    Task DisconnectAsync(DisconnectSnapshot snapshot, CancellationToken ct);

    Task DeleteAsync(UserId userId, CancellationToken ct);
}
