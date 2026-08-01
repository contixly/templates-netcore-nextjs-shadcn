using Template.Domain.Authentication;

namespace Template.Application.Authentication.Ports;

public interface ILocalIdentityGateway
{
    Task<AuthUser> CreateLocalAsync(
        LocalAutomationCredentials credentials,
        CancellationToken cancellationToken);

    Task<AuthUser?> CheckLocalPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<AuthUser> ConfirmEmailAsync(
        UserId userId,
        CancellationToken cancellationToken);

    Task DeleteAsync(UserId userId, CancellationToken cancellationToken);
}
