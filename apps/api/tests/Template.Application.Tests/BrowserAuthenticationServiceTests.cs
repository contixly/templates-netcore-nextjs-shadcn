using Template.Application.Authentication;
using Template.Application.Authentication.Ports;

namespace Template.Application.Tests;

public sealed class BrowserAuthenticationServiceTests
{
    [Fact]
    public async Task AnonymousSessionUsesExplicitAnonymousProjection()
    {
        var service = new BrowserAuthenticationService(
            new AnonymousSessionGateway(),
            new InlineUnitOfWork());

        var state = await service.GetSessionAsync(TestContext.Current.CancellationToken);

        Assert.False(state.Authenticated);
        Assert.Null(state.User);
        Assert.Null(state.Session);
    }

    [Fact]
    public async Task AnonymousLogoutReturnsSessionRequired()
    {
        var service = new BrowserAuthenticationService(
            new AnonymousSessionGateway(),
            new InlineUnitOfWork());

        var result = await service.LogoutAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.SessionRequired, result.Failure);
    }

    private sealed class AnonymousSessionGateway : IBrowserSessionGateway
    {
        public Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AuthenticatedSession?>(null);

        public Task<BrowserSession> SignInAsync(
            AuthUser user,
            string authenticationMethod,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sign-in is not part of this test.");

        public Task<BrowserSession> RenewCurrentAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Renewal is not part of this test.");

        public Task SignOutAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class InlineUnitOfWork : IAuthenticationUnitOfWork
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
