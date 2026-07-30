using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Authentication;

namespace Template.Application.Tests;

public sealed class LocalAutomationAuthServiceTests
{
    [Fact]
    public async Task GeneratedDuplicateRetriesAndReturnsSecondCredentials()
    {
        var identities = new FakeIdentityGateway { DuplicateCreatesRemaining = 1 };
        var sessions = new FakeBrowserSessionGateway();
        var generator = new QueueCredentialGenerator(
            new("First User", "local-agent+first@local-agent.test", "local-first-password"),
            new("Second User", "local-agent+second@local-agent.test", "local-second-password"));
        var transactions = new CountingUnitOfWork();
        var service = new LocalAutomationAuthService(
            identities,
            sessions,
            generator,
            transactions);

        var result = await service.CreateScenarioAsync(
            new CreateLocalScenarioInput(null, null, null),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("local-agent+second@local-agent.test", result.Value!.Credentials.Email);
        Assert.Equal(2, transactions.Executions);
        Assert.Equal(2, identities.CreateAttempts);
        Assert.Equal(LocalAutomationCredentialPolicy.CleanupPath, result.Value.CleanupUrl);
    }

    [Fact]
    public async Task ExplicitDuplicateReturnsConflictWithoutRetry()
    {
        var identities = new FakeIdentityGateway { DuplicateCreatesRemaining = 1 };
        var transactions = new CountingUnitOfWork();
        var service = new LocalAutomationAuthService(
            identities,
            new FakeBrowserSessionGateway(),
            new QueueCredentialGenerator(
                new LocalAutomationCredentials(
                    "Generated",
                    "local-agent+generated@local-agent.test",
                    "local-generated-password")),
            transactions);

        var result = await service.CreateScenarioAsync(
            new CreateLocalScenarioInput(
                "Explicit",
                "local-agent+explicit@local-agent.test",
                "local-explicit-password"),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.UserExists, result.Failure);
        Assert.Equal(1, transactions.Executions);
        Assert.Equal(1, identities.CreateAttempts);
    }

    [Fact]
    public async Task IdentityValidationFailureReturnsInvalidLocalEmailWithoutSigningIn()
    {
        var identities = new FakeIdentityGateway
        {
            CreateException = new LocalIdentityValidationException()
        };
        var sessions = new FakeBrowserSessionGateway();
        var transactions = new CountingUnitOfWork();
        var service = new LocalAutomationAuthService(
            identities,
            sessions,
            new QueueCredentialGenerator(
                new LocalAutomationCredentials(
                    "Generated",
                    "local-agent+generated@local-agent.test",
                    "local-generated-password")),
            transactions);

        var result = await service.CreateScenarioAsync(
            new CreateLocalScenarioInput(
                "Explicit",
                "local-agent+foo!@local-agent.test",
                "local-explicit-password"),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.InvalidLocalEmail, result.Failure);
        Assert.Equal(1, transactions.Executions);
        Assert.Equal(1, identities.CreateAttempts);
        Assert.Equal(0, sessions.SignInCalls);
    }

    [Fact]
    public async Task SignInOutsideNamespaceIsGenericInvalidCredentials()
    {
        var identities = new FakeIdentityGateway();
        var service = new LocalAutomationAuthService(
            identities,
            new FakeBrowserSessionGateway(),
            new QueueCredentialGenerator(
                new LocalAutomationCredentials(
                    "Generated",
                    "local-agent+generated@local-agent.test",
                    "local-generated-password")),
            new CountingUnitOfWork());

        var result = await service.SignInAsync(
            new LocalCredentialInput("person@example.com", "not-used-password"),
            TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.InvalidCredentials, result.Failure);
        Assert.Equal(0, identities.PasswordChecks);
    }

    [Fact]
    public async Task CleanupRejectsAuthenticatedNonLocalUser()
    {
        var sessions = new FakeBrowserSessionGateway
        {
            Current = TestIdentity.Session(isLocalAutomation: false)
        };
        var identities = new FakeIdentityGateway();
        var service = new LocalAutomationAuthService(
            identities,
            sessions,
            new QueueCredentialGenerator(
                new LocalAutomationCredentials(
                    "Generated",
                    "local-agent+generated@local-agent.test",
                    "local-generated-password")),
            new CountingUnitOfWork());

        var result = await service.CleanupAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AuthFailure.LocalUserRequired, result.Failure);
        Assert.Equal(0, identities.DeleteAttempts);
    }

    private sealed class QueueCredentialGenerator(
        params LocalAutomationCredentials[] credentials)
        : ILocalAutomationCredentialGenerator
    {
        private readonly Queue<LocalAutomationCredentials> _credentials = new(credentials);

        public LocalAutomationCredentials Generate() => _credentials.Dequeue();
    }

    private sealed class CountingUnitOfWork : IAuthenticationUnitOfWork
    {
        public int Executions { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            Executions++;
            return await action(cancellationToken);
        }
    }

    private sealed class FakeIdentityGateway : ILocalIdentityGateway
    {
        public int DuplicateCreatesRemaining { get; init; }
        public Exception? CreateException { get; init; }
        public int CreateAttempts { get; private set; }
        public int PasswordChecks { get; private set; }
        public int DeleteAttempts { get; private set; }

        public Task<AuthUser> CreateLocalAsync(
            LocalAutomationCredentials credentials,
            CancellationToken cancellationToken)
        {
            CreateAttempts++;
            if (CreateAttempts <= DuplicateCreatesRemaining)
            {
                throw new DuplicateLocalIdentityException();
            }

            if (CreateException is not null)
            {
                throw CreateException;
            }

            return Task.FromResult(TestIdentity.User(
                email: credentials.Email,
                name: credentials.Name,
                isLocalAutomation: true));
        }

        public Task<AuthUser?> CheckLocalPasswordAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            PasswordChecks++;
            return Task.FromResult<AuthUser?>(
                email == "local-agent+valid@local-agent.test"
                    ? TestIdentity.User(email: email, isLocalAutomation: true)
                    : null);
        }

        public Task DeleteAsync(UserId userId, CancellationToken cancellationToken)
        {
            DeleteAttempts++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBrowserSessionGateway : IBrowserSessionGateway
    {
        public AuthenticatedSession? Current { get; init; }
        public int SignInCalls { get; private set; }
        public int SignOutCalls { get; private set; }

        public Task<AuthenticatedSession?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<BrowserSession> SignInAsync(
            AuthUser user,
            string authenticationMethod,
            CancellationToken cancellationToken)
        {
            SignInCalls++;
            Assert.Equal(BrowserAuthenticationMethods.Local, authenticationMethod);
            return Task.FromResult(TestIdentity.Session(user).Session);
        }

        public Task<BrowserSession> RenewCurrentAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Renewal is not part of this test.");

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            SignOutCalls++;
            return Task.CompletedTask;
        }
    }

    private static class TestIdentity
    {
        public static AuthUser User(
            string email = "local-agent+user@local-agent.test",
            string name = "Local User",
            bool isLocalAutomation = true) =>
            new(
                UserId.New(),
                name,
                email,
                EmailVerified: false,
                Image: null,
                IsLocalAutomation: isLocalAutomation);

        public static AuthenticatedSession Session(bool isLocalAutomation) =>
            Session(User(isLocalAutomation: isLocalAutomation));

        public static AuthenticatedSession Session(AuthUser user)
        {
            var window = SessionWindow.Start(
                new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero),
                TimeSpan.FromDays(7));
            return new AuthenticatedSession(
                user,
                new BrowserSession(
                    SessionId.New(),
                    window.CreatedAt,
                    window.UpdatedAt,
                    window.ExpiresAt,
                    BrowserAuthenticationMethods.Local));
        }
    }
}
