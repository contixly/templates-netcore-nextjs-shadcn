using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;

namespace Template.Infrastructure.Identity;

internal sealed class IdentityGateway(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    TimeProvider timeProvider)
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

        try
        {
            var result = await users.CreateAsync(user, credentials.Password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(error =>
                        error.Code is "DuplicateEmail" or "DuplicateUserName"))
                {
                    throw new DuplicateLocalIdentityException();
                }

                throw new InvalidOperationException(
                    $"Identity user creation failed with codes: {string.Join(',', result.Errors.Select(error => error.Code))}");
            }
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
                  {
                      SqlState: PostgresErrorCodes.UniqueViolation
                  })
        {
            throw new DuplicateLocalIdentityException();
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
}
