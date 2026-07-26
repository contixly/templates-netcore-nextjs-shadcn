namespace Template.Application.Authentication.Ports;

public interface IBrowserSessionGateway
{
    Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<BrowserSession> SignInAsync(AuthUser user, CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
}
