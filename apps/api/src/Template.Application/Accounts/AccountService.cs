using Template.Application.Accounts.Ports;
using Template.Application.Common.Ports;
using Template.Application.Organizations.Ports;
using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Accounts;

public sealed class AccountService(
    IAccountStore accounts,
    IOrganizationUserLifecycleStore organizationLifecycle,
    IApplicationUnitOfWork unitOfWork)
{
    private const int MaximumDisconnectAttempts = 2;
    private const int MaximumDeletionAttempts = 3;

    public Task<AccountSnapshot?> GetAsync(
        UserId userId,
        CancellationToken cancellationToken) =>
        accounts.GetAsync(userId, cancellationToken);

    public async Task<AccountOperationResult<AccountSnapshot>> UpdateDisplayNameAsync(
        UserId userId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var normalized = displayName?.Trim();
        if (normalized is null
            || normalized.Length is < 2 or > 50
            || normalized.Any(char.IsControl))
        {
            return Failed<AccountSnapshot>(AccountFailure.InvalidDisplayName);
        }

        var account = await accounts.UpdateDisplayNameAsync(
            userId,
            normalized,
            cancellationToken);
        return account is null
            ? Failed<AccountSnapshot>(AccountFailure.SessionRequired)
            : Succeeded(account);
    }

    public async Task<IReadOnlyList<AccountConnection>> ListConnectionsAsync(
        UserId userId,
        IReadOnlyCollection<ExternalProvider> configuredProviders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredProviders);

        var connections = await accounts.ListConnectionsAsync(
            userId,
            cancellationToken);
        var configured = configuredProviders.ToHashSet();
        var existingByProvider = connections
            .GroupBy(connection => connection.Provider)
            .ToDictionary(group => group.Key, group => group.First());
        var projected = new List<AccountConnection>(
            configured.Count + existingByProvider.Count);
        var added = new HashSet<ExternalProvider>();

        foreach (var provider in configuredProviders)
        {
            if (!added.Add(provider))
            {
                continue;
            }

            projected.Add(existingByProvider.TryGetValue(provider, out var connection)
                ? connection with { Configured = true }
                : new AccountConnection(
                    provider,
                    Configured: true,
                    Email: null,
                    ConnectedAt: null,
                    LastUsedAt: null));
        }

        foreach (var connection in connections)
        {
            if (added.Add(connection.Provider))
            {
                projected.Add(connection with
                {
                    Configured = configured.Contains(connection.Provider)
                });
            }
        }

        return projected;
    }

    public async Task<AccountOperationResult<AccountDisconnection>> DisconnectAsync(
        UserId userId,
        ExternalProvider? currentAuthenticationProvider,
        ExternalProvider provider,
        IReadOnlyCollection<ExternalProvider> configuredProviders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredProviders);
        var configured = configuredProviders
            .Distinct()
            .ToArray();

        for (var attempt = 0; attempt < MaximumDisconnectAttempts; attempt++)
        {
            var snapshot = await accounts.GetDisconnectSnapshotAsync(
                userId,
                provider,
                configured,
                cancellationToken);
            if (snapshot is null)
            {
                return Failed<AccountDisconnection>(AccountFailure.ConnectionNotFound);
            }

            if (!ExternalConnectionPolicy.CanDisconnect(
                    currentAuthenticationProvider,
                    provider,
                    snapshot.ConfiguredSurvivorCount))
            {
                return Failed<AccountDisconnection>(AccountFailure.ConnectionRequired);
            }

            try
            {
                await accounts.DisconnectAsync(
                    snapshot,
                    configured,
                    cancellationToken);
                return Succeeded(new AccountDisconnection(provider));
            }
            catch (AccountConcurrencyException)
                when (attempt < MaximumDisconnectAttempts - 1)
            {
                // Re-run the complete decision from a fresh atomic snapshot.
            }
            catch (AccountConcurrencyException)
            {
                return await ClassifyDisconnectAfterConflictAsync(
                    userId,
                    currentAuthenticationProvider,
                    provider,
                    configured,
                    cancellationToken);
            }
        }

        return Failed<AccountDisconnection>(AccountFailure.ConcurrencyConflict);
    }

    private async Task<AccountOperationResult<AccountDisconnection>>
        ClassifyDisconnectAfterConflictAsync(
            UserId userId,
            ExternalProvider? currentAuthenticationProvider,
            ExternalProvider provider,
            IReadOnlyCollection<ExternalProvider> configuredProviders,
            CancellationToken cancellationToken)
    {
        var terminal = await accounts.GetDisconnectSnapshotAsync(
            userId,
            provider,
            configuredProviders,
            cancellationToken);
        if (terminal is null)
        {
            return Failed<AccountDisconnection>(
                AccountFailure.ConnectionNotFound);
        }

        return ExternalConnectionPolicy.CanDisconnect(
            currentAuthenticationProvider,
            provider,
            terminal.ConfiguredSurvivorCount)
            ? Failed<AccountDisconnection>(
                AccountFailure.ConcurrencyConflict)
            : Failed<AccountDisconnection>(
                AccountFailure.ConnectionRequired);
    }

    public async Task<AccountOperationResult<AccountDeletion>> DeleteAsync(
        UserId userId,
        string? confirmationEmail,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(userId, cancellationToken);
        if (account is null)
        {
            return Failed<AccountDeletion>(AccountFailure.SessionRequired);
        }

        var normalizedConfirmation = confirmationEmail?.Trim().ToUpperInvariant();
        if (!string.Equals(
                normalizedConfirmation,
                account.PrimaryEmail.NormalizedValue,
                StringComparison.Ordinal))
        {
            return Failed<AccountDeletion>(AccountFailure.ConfirmationMismatch);
        }

        for (var attempt = 0; attempt < MaximumDeletionAttempts; attempt++)
        {
            try
            {
                return await unitOfWork.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        var lifecycle = await organizationLifecycle
                            .PrepareDeletionAsync(
                                userId,
                                transactionCancellationToken);
                        if (lifecycle.OwnershipTransferRequired)
                        {
                            return Failed<AccountDeletion>(
                                AccountFailure
                                    .OrganizationOwnershipTransferRequired);
                        }

                        await accounts.DeleteAsync(
                            userId,
                            transactionCancellationToken);
                        return Succeeded(new AccountDeletion(userId));
                    },
                    cancellationToken);
            }
            catch (OrganizationUserLifecycleConcurrencyException)
                when (attempt < MaximumDeletionAttempts - 1)
            {
                // The membership set changed between discovery and the
                // organization-first lock boundary. Retry from a clean
                // transaction and classify the new complete set.
            }
            catch (OrganizationUserLifecycleConcurrencyException)
            {
                return Failed<AccountDeletion>(
                    AccountFailure.ConcurrencyConflict);
            }
        }

        return Failed<AccountDeletion>(AccountFailure.ConcurrencyConflict);
    }

    private static AccountOperationResult<T> Succeeded<T>(T value)
        where T : class =>
        new(value, null);

    private static AccountOperationResult<T> Failed<T>(AccountFailure failure)
        where T : class =>
        new(null, failure);
}
