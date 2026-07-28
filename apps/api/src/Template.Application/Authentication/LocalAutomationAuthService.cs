using Template.Application.Authentication.Ports;

namespace Template.Application.Authentication;

public sealed class LocalAutomationAuthService(
    ILocalIdentityGateway identities,
    IBrowserSessionGateway sessions,
    ILocalAutomationCredentialGenerator credentialGenerator,
    IAuthenticationUnitOfWork transactions)
{
    public async Task<AuthOperationResult<LocalAutomationScenario>> CreateScenarioAsync(
        CreateLocalScenarioInput input,
        CancellationToken cancellationToken)
    {
        var explicitEmail = !string.IsNullOrWhiteSpace(input.Email);
        var maxAttempts = explicitEmail
            ? 1
            : LocalAutomationCredentialPolicy.GeneratedCollisionAttempts;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var generated = credentialGenerator.Generate();
            var credentials = new LocalAutomationCredentials(
                LocalAutomationCredentialPolicy.NormalizeName(input.Name ?? generated.Name),
                LocalAutomationCredentialPolicy.NormalizeEmail(input.Email ?? generated.Email),
                input.Password ?? generated.Password);

            if (!LocalAutomationCredentialPolicy.IsLocalEmail(credentials.Email))
            {
                return AuthOperationResult<LocalAutomationScenario>.Failed(
                    AuthFailure.InvalidLocalEmail);
            }

            try
            {
                return await transactions.ExecuteAsync(
                    async transactionCancellationToken =>
                    {
                        var user = await identities.CreateLocalAsync(
                            credentials,
                            transactionCancellationToken);
                        var session = await sessions.SignInAsync(
                            user,
                            BrowserAuthenticationMethods.Local,
                            transactionCancellationToken);
                        return AuthOperationResult<LocalAutomationScenario>.Success(
                            new LocalAutomationScenario(
                                user,
                                session,
                                credentials,
                                LocalAutomationCredentialPolicy.CleanupPath));
                    },
                    cancellationToken);
            }
            catch (LocalIdentityValidationException)
            {
                return AuthOperationResult<LocalAutomationScenario>.Failed(
                    AuthFailure.InvalidLocalEmail);
            }
            catch (DuplicateLocalIdentityException)
            {
                if (explicitEmail || attempt == maxAttempts - 1)
                {
                    return AuthOperationResult<LocalAutomationScenario>.Failed(
                        AuthFailure.UserExists);
                }
            }
        }

        return AuthOperationResult<LocalAutomationScenario>.Failed(AuthFailure.UserExists);
    }

    public async Task<AuthOperationResult<AuthenticatedSession>> SignInAsync(
        LocalCredentialInput input,
        CancellationToken cancellationToken)
    {
        var email = LocalAutomationCredentialPolicy.NormalizeEmail(input.Email);
        if (!LocalAutomationCredentialPolicy.IsLocalEmail(email))
        {
            return AuthOperationResult<AuthenticatedSession>.Failed(
                AuthFailure.InvalidCredentials);
        }

        return await transactions.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var user = await identities.CheckLocalPasswordAsync(
                    email,
                    input.Password,
                    transactionCancellationToken);
                if (user is null || !user.IsLocalAutomation)
                {
                    return AuthOperationResult<AuthenticatedSession>.Failed(
                        AuthFailure.InvalidCredentials);
                }

                var session = await sessions.SignInAsync(
                    user,
                    BrowserAuthenticationMethods.Local,
                    transactionCancellationToken);
                return AuthOperationResult<AuthenticatedSession>.Success(
                    new AuthenticatedSession(user, session));
            },
            cancellationToken);
    }

    public async Task<AuthOperationResult<LocalAutomationCleanup>> CleanupAsync(
        CancellationToken cancellationToken)
    {
        var current = await sessions.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return AuthOperationResult<LocalAutomationCleanup>.Failed(
                AuthFailure.SessionRequired);
        }

        if (!current.User.IsLocalAutomation ||
            !LocalAutomationCredentialPolicy.IsLocalEmail(current.User.Email))
        {
            return AuthOperationResult<LocalAutomationCleanup>.Failed(
                AuthFailure.LocalUserRequired);
        }

        return await transactions.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await identities.DeleteAsync(
                    current.User.Id,
                    transactionCancellationToken);
                await sessions.SignOutAsync(transactionCancellationToken);
                return AuthOperationResult<LocalAutomationCleanup>.Success(
                    new LocalAutomationCleanup(DeletedOrganizations: 0));
            },
            cancellationToken);
    }
}
