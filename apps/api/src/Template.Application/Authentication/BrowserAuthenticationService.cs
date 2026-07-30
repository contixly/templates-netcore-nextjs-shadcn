using Template.Application.Authentication.Ports;
using Template.Application.Common.Ports;

namespace Template.Application.Authentication;

public sealed class BrowserAuthenticationService(
    IBrowserSessionGateway sessions,
    IApplicationUnitOfWork transactions)
{
    public async Task<SessionState> GetSessionAsync(CancellationToken cancellationToken)
    {
        var current = await sessions.GetCurrentAsync(cancellationToken);
        return current is null ? SessionState.Anonymous : SessionState.From(current);
    }

    public async Task<AuthOperationResult<SessionState>> LogoutAsync(
        CancellationToken cancellationToken)
    {
        var current = await sessions.GetCurrentAsync(cancellationToken);
        if (current is null)
        {
            return AuthOperationResult<SessionState>.Failed(AuthFailure.SessionRequired);
        }

        return await transactions.ExecuteAsync(
            async transactionCancellationToken =>
            {
                await sessions.SignOutAsync(transactionCancellationToken);
                return AuthOperationResult<SessionState>.Success(SessionState.Anonymous);
            },
            cancellationToken);
    }
}
