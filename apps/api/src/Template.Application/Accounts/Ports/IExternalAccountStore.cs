using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Accounts.Ports;

public interface IExternalAccountStore
{
    Task<ExternalLoginSnapshot?> FindLoginAsync(
        ExternalProvider provider,
        string subject,
        CancellationToken ct);

    Task<AuthUser?> FindUserByEmailAsync(
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
        DateTimeOffset usedAt,
        CancellationToken ct);

    Task UpdateLinkedProfileAsync(
        UserId userId,
        string? displayName,
        Uri? imageUrl,
        CancellationToken ct);
}
