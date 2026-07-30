namespace Template.Application.Authentication.Ports;

public interface IBrowserSessionGateway
{
    Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken);
    Task<BrowserSession> SignInAsync(
        AuthUser user,
        string authenticationMethod,
        CancellationToken cancellationToken);
    Task<BrowserSession> RenewCurrentAsync(CancellationToken cancellationToken);
    Task SignOutAsync(CancellationToken cancellationToken);
}
