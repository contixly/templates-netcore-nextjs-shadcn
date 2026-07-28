using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Accounts.Ports;

public interface IAccountStore
{
    Task<AccountSnapshot?> GetAsync(UserId userId, CancellationToken ct);

    Task<AccountSnapshot> UpdateDisplayNameAsync(
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

    Task DisconnectAsync(DisconnectSnapshot snapshot, CancellationToken ct);

    Task DeleteAsync(UserId userId, CancellationToken ct);
}
