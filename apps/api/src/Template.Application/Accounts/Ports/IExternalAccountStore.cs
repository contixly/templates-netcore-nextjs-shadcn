using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Accounts.Ports;

/// <summary>
/// Persists one external-identity reconciliation attempt. Calls that belong to
/// an attempt run inside the same <c>IApplicationUnitOfWork</c> transaction.
/// </summary>
public interface IExternalAccountStore
{
    /// <summary>
    /// Returns the stable owner snapshot for a provider subject. Persistence
    /// implementations lock an existing login until the surrounding
    /// authentication transaction completes; a new subject returns
    /// <see langword="null"/> without a row lock.
    /// </summary>
    Task<ExternalLoginSnapshot?> FindLoginAsync(
        ExternalProvider provider,
        string subject,
        CancellationToken ct);

    Task<AuthUser?> FindUserByEmailAsync(
        string normalizedEmail,
        CancellationToken ct);

    /// <summary>
    /// Returns whether a provider login owned by <paramref name="userId"/>
    /// currently references the exact verified-email row.
    /// </summary>
    Task<bool> IsEmailVouchedAsync(
        UserId userId,
        string normalizedEmail,
        CancellationToken ct);

    Task<AuthUser> CreateUserAsync(
        ExternalIdentity identity,
        CancellationToken ct);

    Task EnsureVerifiedEmailAsync(
        UserId userId,
        VerifiedEmail email,
        bool primary,
        CancellationToken ct);

    Task AddLoginAsync(
        UserId userId,
        ExternalIdentity identity,
        DateTimeOffset connectedAt,
        bool usedForSignIn,
        CancellationToken ct);

    Task UpdateLoginEmailAsync(
        UserId userId,
        ExternalIdentity identity,
        DateTimeOffset? usedAt,
        CancellationToken ct);

    Task UpdateLinkedProfileAsync(
        UserId userId,
        string? displayName,
        Uri? imageUrl,
        CancellationToken ct);
}
