using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Accounts;

namespace Template.Application.Accounts;

public sealed class ExternalIdentityService(
    IExternalAccountStore accounts,
    IAuthenticationUnitOfWork transactions,
    TimeProvider timeProvider)
{
    private const int MaximumAttempts = 2;

    public async Task<AccountOperationResult<ExternalAuthentication>> ReconcileAsync(
        ExternalIdentity identity,
        ExternalAuthIntent intent,
        AuthenticatedSession? current,
        CancellationToken cancellationToken)
    {
        if (intent == ExternalAuthIntent.Connect && current is null)
        {
            return Failed(AccountFailure.SessionRequired);
        }

        for (var attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            try
            {
                return await transactions.ExecuteAsync(
                    transactionCancellationToken => ReconcileAttemptAsync(
                        identity,
                        intent,
                        current,
                        transactionCancellationToken),
                    cancellationToken);
            }
            catch (AccountConcurrencyException) when (attempt < MaximumAttempts - 1)
            {
                // A fresh transaction re-reads the stable provider subject and email ownership.
            }
            catch (AccountConcurrencyException)
            {
                return Failed(AccountFailure.ConcurrencyConflict);
            }
        }

        return Failed(AccountFailure.ConcurrencyConflict);
    }

    private async Task<AccountOperationResult<ExternalAuthentication>> ReconcileAttemptAsync(
        ExternalIdentity identity,
        ExternalAuthIntent intent,
        AuthenticatedSession? current,
        CancellationToken cancellationToken)
    {
        var login = await accounts.FindLoginAsync(
            identity.Provider,
            identity.Subject,
            cancellationToken);

        return login is null
            ? await ReconcileNewLoginAsync(identity, intent, current, cancellationToken)
            : await ReconcileExistingLoginAsync(
                identity,
                intent,
                current,
                login,
                cancellationToken);
    }

    private async Task<AccountOperationResult<ExternalAuthentication>> ReconcileNewLoginAsync(
        ExternalIdentity identity,
        ExternalAuthIntent intent,
        AuthenticatedSession? current,
        CancellationToken cancellationToken)
    {
        var emailOwner = await accounts.FindUserByEmailAsync(
            identity.Email.NormalizedValue,
            cancellationToken);

        AuthUser user;
        var createdUser = false;

        if (intent == ExternalAuthIntent.Connect)
        {
            user = current!.User;
            var ownership = ExternalConnectionPolicy.DecideEmailOwnership(
                user.Id,
                emailOwner?.Id);

            if (ownership == EmailOwnershipDecision.ConflictWithOtherUser)
            {
                return Failed(AccountFailure.EmailConflict);
            }

            if (ownership == EmailOwnershipDecision.AttachSecondary)
            {
                await accounts.EnsureVerifiedEmailAsync(
                    user.Id,
                    identity.Email,
                    primary: false,
                    cancellationToken);
            }
        }
        else if (emailOwner is not null)
        {
            user = emailOwner;
        }
        else
        {
            user = await accounts.CreateUserAsync(identity, cancellationToken);
            createdUser = true;
            await accounts.EnsureVerifiedEmailAsync(
                user.Id,
                identity.Email,
                primary: true,
                cancellationToken);
        }

        var connectedAt = timeProvider.GetUtcNow();
        await accounts.AddLoginAsync(
            user.Id,
            identity,
            connectedAt,
            usedForSignIn: intent == ExternalAuthIntent.SignIn,
            cancellationToken);
        await accounts.UpdateLinkedProfileAsync(
            user.Id,
            identity.DisplayName,
            identity.ImageUrl,
            cancellationToken);

        var effectiveUser = user with
        {
            Name = identity.DisplayName ?? user.Name,
            Image = identity.ImageUrl?.AbsoluteUri ?? user.Image
        };

        return Succeeded(new ExternalAuthentication(
            effectiveUser,
            identity.Provider,
            createdUser,
            AddedConnection: true));
    }

    private async Task<AccountOperationResult<ExternalAuthentication>> ReconcileExistingLoginAsync(
        ExternalIdentity identity,
        ExternalAuthIntent intent,
        AuthenticatedSession? current,
        ExternalLoginSnapshot login,
        CancellationToken cancellationToken)
    {
        var owner = await accounts.FindUserByEmailAsync(
            login.Email.NormalizedValue,
            cancellationToken);
        if (owner is null || owner.Id != login.UserId)
        {
            return Failed(AccountFailure.IdentityConflict);
        }

        if (intent == ExternalAuthIntent.Connect && current!.User.Id != login.UserId)
        {
            return Failed(AccountFailure.IdentityConflict);
        }

        AuthUser? incomingEmailOwner;
        if (identity.Email.NormalizedValue == login.Email.NormalizedValue)
        {
            incomingEmailOwner = owner;
        }
        else
        {
            incomingEmailOwner = await accounts.FindUserByEmailAsync(
                identity.Email.NormalizedValue,
                cancellationToken);
        }

        if (incomingEmailOwner is not null && incomingEmailOwner.Id != login.UserId)
        {
            return Failed(AccountFailure.EmailConflict);
        }

        if (incomingEmailOwner is null)
        {
            await accounts.EnsureVerifiedEmailAsync(
                login.UserId,
                identity.Email,
                primary: false,
                cancellationToken);
        }

        await accounts.UpdateLoginEmailAsync(
            login.UserId,
            identity,
            intent == ExternalAuthIntent.SignIn
                ? timeProvider.GetUtcNow()
                : null,
            cancellationToken);

        return Succeeded(new ExternalAuthentication(
            owner,
            identity.Provider,
            CreatedUser: false,
            AddedConnection: false));
    }

    private static AccountOperationResult<ExternalAuthentication> Succeeded(
        ExternalAuthentication authentication) =>
        new(authentication, null);

    private static AccountOperationResult<ExternalAuthentication> Failed(
        AccountFailure failure) =>
        new(null, failure);
}
