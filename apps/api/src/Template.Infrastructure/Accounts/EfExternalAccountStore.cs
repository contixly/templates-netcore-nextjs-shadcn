using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Accounts;

internal sealed class EfExternalAccountStore(
    TemplateDbContext db,
    TimeProvider timeProvider)
    : IExternalAccountStore
{
    public async Task<ExternalLoginSnapshot?> FindLoginAsync(
        ExternalProvider provider,
        string subject,
        CancellationToken ct)
    {
        EnsureActiveAuthenticationTransaction();
        var login = await db.UserLogins
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM auth.user_logins
                WHERE login_provider = {provider.Value}
                  AND provider_key = {subject}
                FOR UPDATE
                """)
            .AsNoTracking()
            .SingleOrDefaultAsync(ct);
        if (login is null)
        {
            return null;
        }

        var email = await db.UserEmails.AsNoTracking()
            .SingleAsync(row => row.Id == login.VerifiedEmailId, ct);
        return new ExternalLoginSnapshot(
            new UserId(login.UserId),
            provider,
            login.ProviderKey,
            new VerifiedEmail(email.Email, email.NormalizedEmail),
            login.ConnectedAt,
            login.LastUsedAt);
    }

    public async Task<AuthUser?> FindUserByEmailAsync(
        string normalizedEmail,
        CancellationToken ct)
    {
        var user = await (
            from email in db.UserEmails.AsNoTracking()
            join candidate in db.Users.AsNoTracking()
                on email.UserId equals candidate.Id
            where email.NormalizedEmail == normalizedEmail
            select candidate).SingleOrDefaultAsync(ct);
        return user is null ? null : Map(user);
    }

    public Task<bool> IsEmailVouchedAsync(
        UserId userId,
        string normalizedEmail,
        CancellationToken ct) =>
        (
            from login in db.UserLogins.AsNoTracking()
            join email in db.UserEmails.AsNoTracking()
                on login.VerifiedEmailId equals email.Id
            where login.UserId == userId.Value
                && email.UserId == userId.Value
                && email.NormalizedEmail == normalizedEmail
            select login)
        .AnyAsync(ct);

    public async Task<AuthUser> CreateUserAsync(
        ExternalIdentity identity,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var displayName = identity.DisplayName ?? identity.Email.Value;
        if (displayName.Length > 50)
        {
            displayName = displayName[..50];
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = identity.Email.Value,
            NormalizedUserName = identity.Email.NormalizedValue,
            Email = identity.Email.Value,
            NormalizedEmail = identity.Email.NormalizedValue,
            EmailConfirmed = true,
            DisplayName = displayName,
            ImageUrl = identity.ImageUrl?.AbsoluteUri,
            IsLocalAutomation = false,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);
        await SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task EnsureVerifiedEmailAsync(
        UserId userId,
        VerifiedEmail email,
        bool primary,
        CancellationToken ct)
    {
        var existing = await db.UserEmails.SingleOrDefaultAsync(
            row => row.NormalizedEmail == email.NormalizedValue,
            ct);
        if (existing is not null)
        {
            if (existing.UserId != userId.Value)
            {
                throw new AccountConcurrencyException();
            }

            if (primary && !existing.IsPrimary)
            {
                existing.IsPrimary = true;
                existing.Email = email.Value;
                await MirrorPrimaryEmailAsync(userId.Value, email, ct);
                await SaveChangesAsync(ct);
            }

            return;
        }

        db.UserEmails.Add(new UserEmailEntity
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            UserId = userId.Value,
            Email = email.Value,
            NormalizedEmail = email.NormalizedValue,
            IsPrimary = primary,
            CreatedAt = timeProvider.GetUtcNow()
        });
        if (primary)
        {
            await MirrorPrimaryEmailAsync(userId.Value, email, ct);
        }

        await SaveChangesAsync(ct);
    }

    public async Task AddLoginAsync(
        UserId userId,
        ExternalIdentity identity,
        DateTimeOffset connectedAt,
        bool usedForSignIn,
        CancellationToken ct)
    {
        var emailId = await db.UserEmails
            .Where(row =>
                row.UserId == userId.Value
                && row.NormalizedEmail == identity.Email.NormalizedValue)
            .Select(row => row.Id)
            .SingleAsync(ct);
        db.UserLogins.Add(new ApplicationUserLogin
        {
            UserId = userId.Value,
            LoginProvider = identity.Provider.Value,
            ProviderKey = identity.Subject,
            ProviderDisplayName = identity.Provider.Value,
            VerifiedEmailId = emailId,
            ConnectedAt = connectedAt,
            LastUsedAt = usedForSignIn ? connectedAt : null
        });
        await SaveChangesAsync(ct);
    }

    public async Task UpdateLoginEmailAsync(
        UserId userId,
        ExternalIdentity identity,
        DateTimeOffset? usedAt,
        CancellationToken ct)
    {
        EnsureActiveAuthenticationTransaction();
        var login = await db.UserLogins
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM auth.user_logins
                WHERE user_id = {userId.Value}
                  AND login_provider = {identity.Provider.Value}
                  AND provider_key = {identity.Subject}
                FOR UPDATE
                """)
            .SingleAsync(ct);
        var email = await db.UserEmails.SingleAsync(
            row =>
                row.UserId == userId.Value
                && row.NormalizedEmail == identity.Email.NormalizedValue,
            ct);
        var previousEmailId = login.VerifiedEmailId;
        login.VerifiedEmailId = email.Id;
        if (usedAt is not null)
        {
            login.LastUsedAt = usedAt;
        }

        await SaveChangesAsync(ct);

        if (previousEmailId != email.Id)
        {
            await db.UserEmails
                .Where(row =>
                    row.Id == previousEmailId
                    && row.UserId == userId.Value
                    && !row.IsPrimary
                    && !db.UserLogins.Any(loginRow =>
                        loginRow.VerifiedEmailId == row.Id))
                .ExecuteDeleteAsync(ct);
        }
    }

    public async Task UpdateLinkedProfileAsync(
        UserId userId,
        string? displayName,
        Uri? imageUrl,
        CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(row => row.Id == userId.Value, ct);
        if (displayName is not null)
        {
            user.DisplayName = displayName;
        }

        if (imageUrl is not null)
        {
            user.ImageUrl = imageUrl.AbsoluteUri;
        }

        user.UpdatedAt = timeProvider.GetUtcNow();
        await SaveChangesAsync(ct);
    }

    private async Task MirrorPrimaryEmailAsync(
        Guid userId,
        VerifiedEmail email,
        CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(row => row.Id == userId, ct);
        user.Email = email.Value;
        user.NormalizedEmail = email.NormalizedValue;
        user.EmailConfirmed = true;
    }

    private async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            db.ChangeTracker.Clear();
            throw new AccountConcurrencyException();
        }
    }

    private void EnsureActiveAuthenticationTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "External identity reconciliation requires an active authentication transaction.");
        }
    }

    private static AuthUser Map(ApplicationUser user) =>
        new(
            new UserId(user.Id),
            user.DisplayName,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            user.ImageUrl,
            user.IsLocalAutomation);
}
