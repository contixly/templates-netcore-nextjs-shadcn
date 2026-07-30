using Microsoft.EntityFrameworkCore;
using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Accounts;

internal sealed class EfAccountStore(
    TemplateDbContext db,
    TimeProvider timeProvider)
    : IAccountStore
{
    public async Task<AccountSnapshot?> GetAsync(
        UserId userId,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == userId.Value,
            ct);
        if (user is null)
        {
            return null;
        }

        var emails = await db.UserEmails.AsNoTracking()
            .Where(row => row.UserId == userId.Value)
            .OrderByDescending(row => row.IsPrimary)
            .ThenBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArrayAsync(ct);
        var primary = emails.Single(row => row.IsPrimary);
        var logins = await db.UserLogins.AsNoTracking()
            .Where(row => row.UserId == userId.Value)
            .OrderBy(row => row.LoginProvider)
            .ToArrayAsync(ct);
        var providersByEmail = logins
            .GroupBy(row => row.VerifiedEmailId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ExternalProvider>)group
                    .Select(row => ParseProvider(row.LoginProvider))
                    .Distinct()
                    .ToArray());

        return new AccountSnapshot(
            Map(user),
            Map(primary),
            emails
                .Select(email => new AccountEmail(
                    Map(email),
                    email.IsPrimary,
                    providersByEmail.GetValueOrDefault(
                        email.Id,
                        Array.Empty<ExternalProvider>())))
                .ToArray(),
            user.CreatedAt);
    }

    public async Task<AccountSnapshot?> UpdateDisplayNameAsync(
        UserId userId,
        string displayName,
        CancellationToken ct)
    {
        var updated = await db.Users
            .Where(row => row.Id == userId.Value)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.DisplayName, displayName)
                    .SetProperty(row => row.UpdatedAt, timeProvider.GetUtcNow()),
                ct);
        if (updated == 0)
        {
            return null;
        }

        return await GetAsync(userId, ct);
    }

    public async Task<IReadOnlyList<AccountConnection>> ListConnectionsAsync(
        UserId userId,
        CancellationToken ct)
    {
        var rows = await (
            from login in db.UserLogins.AsNoTracking()
            join email in db.UserEmails.AsNoTracking()
                on login.VerifiedEmailId equals email.Id
            where login.UserId == userId.Value
            orderby login.LoginProvider
            select new
            {
                login.LoginProvider,
                email.Email,
                email.NormalizedEmail,
                login.ConnectedAt,
                login.LastUsedAt
            }).ToArrayAsync(ct);

        return rows
            .Select(row => new AccountConnection(
                ParseProvider(row.LoginProvider),
                Configured: false,
                new VerifiedEmail(row.Email, row.NormalizedEmail),
                row.ConnectedAt,
                row.LastUsedAt))
            .ToArray();
    }

    public async Task<DisconnectSnapshot?> GetDisconnectSnapshotAsync(
        UserId userId,
        ExternalProvider provider,
        IReadOnlyCollection<ExternalProvider> configuredProviders,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuredProviders);
        var login = await (
            from row in db.UserLogins.AsNoTracking()
            join email in db.UserEmails.AsNoTracking()
                on row.VerifiedEmailId equals email.Id
            where row.UserId == userId.Value
                && row.LoginProvider == provider.Value
            select new
            {
                email.Email,
                email.NormalizedEmail,
                email.IsPrimary
            }).SingleOrDefaultAsync(ct);
        if (login is null)
        {
            return null;
        }

        var configured = configuredProviders
            .Select(candidate => candidate.Value)
            .ToHashSet(StringComparer.Ordinal);
        var connectedProviders = await db.UserLogins.AsNoTracking()
            .Where(row => row.UserId == userId.Value)
            .Select(row => row.LoginProvider)
            .ToArrayAsync(ct);
        var configuredSurvivorCount = connectedProviders.Count(candidate =>
            candidate != provider.Value && configured.Contains(candidate));
        return new DisconnectSnapshot(
            userId,
            provider,
            new VerifiedEmail(login.Email, login.NormalizedEmail),
            login.IsPrimary,
            configuredSurvivorCount);
    }

    public async Task DisconnectAsync(
        DisconnectSnapshot snapshot,
        IReadOnlyCollection<ExternalProvider> configuredProviders,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(configuredProviders);
        var configured = configuredProviders
            .Select(provider => provider.Value)
            .ToHashSet(StringComparer.Ordinal);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var lockedLogins = await db.UserLogins
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM auth.user_logins
                    WHERE user_id = {snapshot.UserId.Value}
                    ORDER BY login_provider, provider_key
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .ToArrayAsync(ct);
            var login = lockedLogins.SingleOrDefault(
                row => row.LoginProvider == snapshot.Provider.Value);
            if (login is null)
            {
                throw new AccountConcurrencyException();
            }

            var email = await db.UserEmails
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM auth.user_emails
                    WHERE id = {login.VerifiedEmailId}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .SingleAsync(ct);
            var configuredSurvivorCount = lockedLogins.Count(row =>
                row.LoginProvider != snapshot.Provider.Value
                && configured.Contains(row.LoginProvider));
            if (configuredSurvivorCount < 1
                || configuredSurvivorCount != snapshot.ConfiguredSurvivorCount
                || email.UserId != snapshot.UserId.Value
                || email.IsPrimary != snapshot.EmailIsPrimary
                || email.Email != snapshot.Email.Value
                || email.NormalizedEmail != snapshot.Email.NormalizedValue)
            {
                throw new AccountConcurrencyException();
            }

            var deleted = await db.UserLogins
                .Where(row =>
                    row.LoginProvider == login.LoginProvider
                    && row.ProviderKey == login.ProviderKey
                    && row.UserId == snapshot.UserId.Value
                    && row.VerifiedEmailId == email.Id)
                .ExecuteDeleteAsync(ct);
            if (deleted != 1)
            {
                throw new AccountConcurrencyException();
            }

            if (!email.IsPrimary)
            {
                await db.UserEmails
                    .Where(row =>
                        row.Id == email.Id
                        && row.UserId == snapshot.UserId.Value
                        && !row.IsPrimary
                        && !db.UserLogins.Any(remaining =>
                            remaining.VerifiedEmailId == row.Id))
                    .ExecuteDeleteAsync(ct);
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the operation failure when rollback cannot complete.
            }
            finally
            {
                db.ChangeTracker.Clear();
            }

            throw;
        }
    }

    public async Task DeleteAsync(UserId userId, CancellationToken ct)
    {
        await db.Users
            .Where(row => row.Id == userId.Value)
            .ExecuteDeleteAsync(ct);
    }

    private static ExternalProvider ParseProvider(string value) =>
        ExternalProvider.TryParse(value, out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"Stored external provider '{value}' is not supported.");

    private static VerifiedEmail Map(UserEmailEntity email) =>
        new(email.Email, email.NormalizedEmail);

    private static AuthUser Map(ApplicationUser user) =>
        new(
            new UserId(user.Id),
            user.DisplayName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.ImageUrl,
            user.IsLocalAutomation);
}
