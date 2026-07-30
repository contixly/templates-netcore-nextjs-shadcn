using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;
using Template.Infrastructure.Persistence;

namespace Template.Infrastructure.Identity;

internal sealed class IdentityGateway(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    TimeProvider timeProvider,
    TemplateDbContext db)
    : ILocalIdentityGateway
{
    public async Task<AuthUser> CreateLocalAsync(
        LocalAutomationCredentials credentials,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(now),
            UserName = credentials.Email,
            Email = credentials.Email,
            DisplayName = credentials.Name,
            EmailConfirmed = false,
            IsLocalAutomation = true,
            LockoutEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var result = await users.CreateAsync(user, credentials.Password);
            if (!result.Succeeded)
            {
                var errors = result.Errors.ToArray();
                if (errors.Length > 0 && errors.All(IsDuplicateError))
                {
                    throw new DuplicateLocalIdentityException();
                }

                if (errors.Length > 0 && errors.All(IsKnownInputValidationError))
                {
                    throw new LocalIdentityValidationException();
                }

                throw new InvalidOperationException(
                    "Identity user creation failed unexpectedly.");
            }

            db.UserEmails.Add(new UserEmailEntity
            {
                Id = Guid.CreateVersion7(now),
                UserId = user.Id,
                Email = user.Email
                    ?? throw new InvalidOperationException(
                        "Identity did not retain the required local email."),
                NormalizedEmail = user.NormalizedEmail
                    ?? throw new InvalidOperationException(
                        "Identity did not normalize the required local email."),
                IsPrimary = true,
                CreatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            await RollbackOwnedTransactionAsync(transaction);
            throw new DuplicateLocalIdentityException();
        }
        catch
        {
            await RollbackOwnedTransactionAsync(transaction);
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return Map(user);
    }

    public async Task<AuthUser?> CheckLocalPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.IsLocalAutomation)
        {
            return null;
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);
        return result.Succeeded ? Map(user) : null;
    }

    public async Task DeleteAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            return;
        }

        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Identity user deletion failed with codes: {string.Join(',', result.Errors.Select(error => error.Code))}");
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

    private static bool IsDuplicateError(IdentityError error) =>
        error.Code is "DuplicateEmail" or "DuplicateUserName";

    private static bool IsKnownInputValidationError(IdentityError error) =>
        error.Code is
            "InvalidUserName" or
            "InvalidEmail" or
            "PasswordTooShort" or
            "PasswordRequiresNonAlphanumeric" or
            "PasswordRequiresDigit" or
            "PasswordRequiresLower" or
            "PasswordRequiresUpper" or
            "PasswordRequiresUniqueChars";

    private async Task RollbackOwnedTransactionAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the creation failure when rollback cannot complete.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }
}
