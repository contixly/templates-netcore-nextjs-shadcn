using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Application.Authentication.Ports;
using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Tests.Accounts;

public sealed class ExternalIdentityServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AnonymousNewUserGetsPrimaryEmailAndSignInConnection()
    {
        var fixture = new Fixture();

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Null(result.Failure);
        Assert.True(result.Value!.CreatedUser);
        Assert.True(result.Value.AddedConnection);
        Assert.Equal(ExternalProvider.Google, result.Value.Provider);
        Assert.Single(fixture.Store.CreatedIdentities);
        var email = Assert.Single(fixture.Store.EnsuredEmails);
        Assert.True(email.Primary);
        var login = Assert.Single(fixture.Store.AddedLogins);
        Assert.True(login.UsedForSignIn);
        Assert.Equal(Now, login.ConnectedAt);
        Assert.Equal(1, fixture.Transactions.ExecutionCount);
    }

    [Fact]
    public async Task AnonymousVerifiedPrimaryEmailImplicitlyLinksItsOwner()
    {
        var fixture = new Fixture();
        var owner = fixture.Store.AddUser(Email(), primary: true);

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Equal(owner.Id, result.Value!.User.Id);
        Assert.True(result.Value.AddedConnection);
        Assert.False(result.Value.CreatedUser);
        Assert.Equal("Provider Name", result.Value.User.Name);
        Assert.Equal("https://cdn.example.test/avatar.png", result.Value.User.Image);
        Assert.Empty(fixture.Store.EnsuredEmails);
        Assert.Equal(owner.Id, Assert.Single(fixture.Store.AddedLogins).UserId);
    }

    [Fact]
    public async Task AnonymousVerifiedSecondaryEmailImplicitlyLinksItsOwner()
    {
        var fixture = new Fixture();
        var owner = fixture.Store.AddUser(Email(), primary: false);

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Equal(owner.Id, result.Value!.User.Id);
        Assert.True(result.Value.AddedConnection);
        Assert.False(result.Value.CreatedUser);
        Assert.Empty(fixture.Store.EnsuredEmails);
        Assert.Equal(owner.Id, Assert.Single(fixture.Store.AddedLogins).UserId);
    }

    [Fact]
    public async Task AuthenticatedConnectReusesEmailAlreadyOwnedByCurrentUser()
    {
        var fixture = new Fixture();
        var current = fixture.Session(fixture.Store.AddUser(Email(), primary: true));

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.Connect,
            current,
            Ct);

        Assert.Equal(current.User.Id, result.Value!.User.Id);
        Assert.False(result.Value.CreatedUser);
        Assert.True(result.Value.AddedConnection);
        Assert.Equal("Provider Name", result.Value.User.Name);
        Assert.Equal("https://cdn.example.test/avatar.png", result.Value.User.Image);
        Assert.Empty(fixture.Store.EnsuredEmails);
        var login = Assert.Single(fixture.Store.AddedLogins);
        Assert.Equal(current.User.Id, login.UserId);
        Assert.False(login.UsedForSignIn);
    }

    [Fact]
    public async Task AuthenticatedConnectAddsFreeDifferentEmailAsSecondary()
    {
        var fixture = new Fixture();
        var current = fixture.Session(fixture.Store.AddUser(
            VerifiedEmail.Create("current@example.test"),
            primary: true));

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.Connect,
            current,
            Ct);

        Assert.Null(result.Failure);
        var email = Assert.Single(fixture.Store.EnsuredEmails);
        Assert.Equal(current.User.Id, email.UserId);
        Assert.Equal(Email(), email.Email);
        Assert.False(email.Primary);
        Assert.Equal(current.User.Id, Assert.Single(fixture.Store.AddedLogins).UserId);
    }

    [Fact]
    public async Task AuthenticatedConnectRejectsEmailOwnedByAnotherUser()
    {
        var fixture = new Fixture();
        var current = fixture.Session(fixture.Store.AddUser(
            VerifiedEmail.Create("current@example.test"),
            primary: true));
        fixture.Store.AddUser(Email(), primary: false);

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.Connect,
            current,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.EmailConflict, result.Failure);
        Assert.Empty(fixture.Store.EnsuredEmails);
        Assert.Empty(fixture.Store.AddedLogins);
        Assert.Empty(fixture.Store.ProfileUpdates);
    }

    [Fact]
    public async Task ExistingSubjectSignsInItsStableOwnerBeforeConsideringEmail()
    {
        var fixture = new Fixture();
        var owner = fixture.Store.AddUser(Email(), primary: true);
        fixture.Store.AddLogin(owner, Identity(), Now.AddDays(-2));

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Equal(owner.Id, result.Value!.User.Id);
        Assert.False(result.Value.CreatedUser);
        Assert.False(result.Value.AddedConnection);
        Assert.Single(fixture.Store.UpdatedLogins);
        Assert.Empty(fixture.Store.AddedLogins);
        Assert.Equal("find-login", fixture.Store.Operations[0]);
        Assert.Equal("find-email", fixture.Store.Operations[1]);
    }

    [Fact]
    public async Task SameOwnerExistingLoginConnectPreservesLastUsedAt()
    {
        var fixture = new Fixture();
        var lastUsedAt = Now.AddDays(-1);
        var owner = fixture.Store.AddUser(Email(), primary: true);
        var identity = Identity();
        fixture.Store.AddLogin(owner, identity, Now.AddDays(-2), lastUsedAt);

        var result = await fixture.Subject.ReconcileAsync(
            identity,
            ExternalAuthIntent.Connect,
            fixture.Session(owner),
            Ct);

        Assert.Null(result.Failure);
        Assert.False(result.Value!.AddedConnection);
        var update = Assert.Single(fixture.Store.UpdatedLogins);
        Assert.Null(update.UsedAt);
        Assert.Equal(Email(), update.Identity.Email);
        Assert.Equal(lastUsedAt, fixture.Store.GetLogin(identity).LastUsedAt);
    }

    [Fact]
    public async Task SameOwnerExistingLoginConnectChangesEmailAndPreservesLastUsedAt()
    {
        var fixture = new Fixture();
        var oldEmail = VerifiedEmail.Create("old@example.test");
        var lastUsedAt = Now.AddDays(-1);
        var owner = fixture.Store.AddUser(oldEmail, primary: true);
        var oldIdentity = Identity(email: oldEmail);
        fixture.Store.AddLogin(owner, oldIdentity, Now.AddDays(-2), lastUsedAt);
        var changedIdentity = Identity();

        var result = await fixture.Subject.ReconcileAsync(
            changedIdentity,
            ExternalAuthIntent.Connect,
            fixture.Session(owner),
            Ct);

        Assert.Null(result.Failure);
        var email = Assert.Single(fixture.Store.EnsuredEmails);
        Assert.Equal(owner.Id, email.UserId);
        Assert.False(email.Primary);
        var update = Assert.Single(fixture.Store.UpdatedLogins);
        Assert.Null(update.UsedAt);
        Assert.Equal(changedIdentity.Email, update.Identity.Email);
        var persistedLogin = fixture.Store.GetLogin(changedIdentity);
        Assert.Equal(changedIdentity.Email, persistedLogin.Email);
        Assert.Equal(lastUsedAt, persistedLogin.LastUsedAt);
    }

    [Fact]
    public async Task ExistingSubjectAdoptsChangedFreeEmailAsSecondary()
    {
        var fixture = new Fixture();
        var oldEmail = VerifiedEmail.Create("old@example.test");
        var owner = fixture.Store.AddUser(oldEmail, primary: true);
        fixture.Store.AddLogin(owner, Identity(email: oldEmail), Now.AddDays(-2));

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Equal(owner.Id, result.Value!.User.Id);
        var email = Assert.Single(fixture.Store.EnsuredEmails);
        Assert.Equal(owner.Id, email.UserId);
        Assert.False(email.Primary);
        var update = Assert.Single(fixture.Store.UpdatedLogins);
        Assert.Equal(Email(), update.Identity.Email);
        Assert.Equal(Now, update.UsedAt);
        Assert.Empty(fixture.Store.ProfileUpdates);
    }

    [Fact]
    public async Task ExistingSubjectRejectsChangedEmailOwnedByAnotherUser()
    {
        var fixture = new Fixture();
        var oldEmail = VerifiedEmail.Create("old@example.test");
        var owner = fixture.Store.AddUser(oldEmail, primary: true);
        fixture.Store.AddLogin(owner, Identity(email: oldEmail), Now.AddDays(-2));
        fixture.Store.AddUser(Email(), primary: false);

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.EmailConflict, result.Failure);
        Assert.Empty(fixture.Store.EnsuredEmails);
        Assert.Empty(fixture.Store.UpdatedLogins);
        Assert.Equal("find-login", fixture.Store.Operations[0]);
    }

    [Fact]
    public async Task ConnectWithoutCurrentSessionIsRejectedBeforeTransaction()
    {
        var fixture = new Fixture();

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.Connect,
            null,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.SessionRequired, result.Failure);
        Assert.Equal(0, fixture.Transactions.ExecutionCount);
        Assert.Empty(fixture.Store.Operations);
    }

    [Fact]
    public async Task UniqueConflictRetriesOnceFromFreshReads()
    {
        var fixture = new Fixture();
        fixture.Store.CreateFailures.Enqueue(new AccountConcurrencyException());

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Null(result.Failure);
        Assert.True(result.Value!.CreatedUser);
        Assert.Equal(2, fixture.Transactions.ExecutionCount);
        Assert.Equal(2, fixture.Store.Operations.Count(value => value == "find-login"));
        Assert.Equal(2, fixture.Store.Operations.Count(value => value == "find-email"));
        Assert.Equal(2, fixture.Store.Operations.Count(value => value == "create-user"));
    }

    [Fact]
    public async Task SecondUniqueConflictReturnsStableConcurrencyFailure()
    {
        var fixture = new Fixture();
        fixture.Store.CreateFailures.Enqueue(new AccountConcurrencyException());
        fixture.Store.CreateFailures.Enqueue(new AccountConcurrencyException());

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConcurrencyConflict, result.Failure);
        Assert.Equal(2, fixture.Transactions.ExecutionCount);
    }

    [Fact]
    public async Task ExistingSubjectOwnedByDifferentCurrentUserIsIdentityConflict()
    {
        var fixture = new Fixture();
        var owner = fixture.Store.AddUser(Email(), primary: true);
        fixture.Store.AddLogin(owner, Identity(), Now.AddDays(-2));
        var current = fixture.Session(fixture.Store.AddUser(
            VerifiedEmail.Create("current@example.test"),
            primary: true));

        var result = await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.Connect,
            current,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.IdentityConflict, result.Failure);
        Assert.Empty(fixture.Store.UpdatedLogins);
    }

    [Fact]
    public async Task ProfileIsUpdatedForNewLinkButNotExistingSubject()
    {
        var fixture = new Fixture();
        var owner = fixture.Store.AddUser(Email(), primary: true);

        await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        var profile = Assert.Single(fixture.Store.ProfileUpdates);
        Assert.Equal(owner.Id, profile.UserId);
        Assert.Equal("Provider Name", profile.DisplayName);
        Assert.Equal(new Uri("https://cdn.example.test/avatar.png"), profile.ImageUrl);

        fixture.Store.ProfileUpdates.Clear();
        await fixture.Subject.ReconcileAsync(
            Identity(),
            ExternalAuthIntent.SignIn,
            null,
            Ct);

        Assert.Empty(fixture.Store.ProfileUpdates);
    }

    private static VerifiedEmail Email() => VerifiedEmail.Create("person@example.test");

    private static ExternalIdentity Identity(
        VerifiedEmail? email = null,
        string subject = "provider-subject") =>
        new(
            ExternalProvider.Google,
            subject,
            email ?? Email(),
            "Provider Name",
            new Uri("https://cdn.example.test/avatar.png"));

    private sealed class Fixture
    {
        public Fixture()
        {
            Store = new FakeExternalAccountStore();
            Transactions = new FakeAuthenticationUnitOfWork();
            Subject = new ExternalIdentityService(
                Store,
                Transactions,
                new FixedTimeProvider(Now));
        }

        public FakeExternalAccountStore Store { get; }

        public FakeAuthenticationUnitOfWork Transactions { get; }

        public ExternalIdentityService Subject { get; }

        public AuthenticatedSession Session(AuthUser user) =>
            new(
                user,
                new BrowserSession(
                    new SessionId(Guid.NewGuid()),
                    Now.AddHours(-1),
                    Now.AddHours(-1),
                    Now.AddHours(1)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAuthenticationUnitOfWork : IAuthenticationUnitOfWork
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken);
        }
    }

    private sealed class FakeExternalAccountStore : IExternalAccountStore
    {
        private readonly Dictionary<string, (AuthUser User, bool Primary)> usersByEmail = [];
        private readonly Dictionary<(ExternalProvider Provider, string Subject), ExternalLoginSnapshot>
            logins = [];

        public List<string> Operations { get; } = [];
        public List<ExternalIdentity> CreatedIdentities { get; } = [];
        public List<(UserId UserId, VerifiedEmail Email, bool Primary)> EnsuredEmails { get; } = [];
        public List<(UserId UserId, ExternalIdentity Identity, DateTimeOffset ConnectedAt, bool UsedForSignIn)>
            AddedLogins { get; } = [];
        public List<(UserId UserId, ExternalIdentity Identity, DateTimeOffset? UsedAt)> UpdatedLogins { get; } = [];
        public List<(UserId UserId, string? DisplayName, Uri? ImageUrl)> ProfileUpdates { get; } = [];
        public Queue<Exception> CreateFailures { get; } = [];

        public AuthUser AddUser(VerifiedEmail email, bool primary)
        {
            var user = new AuthUser(
                UserId.New(),
                "Existing User",
                primary ? email.Value : "primary@example.test",
                true,
                null,
                false);
            usersByEmail[email.NormalizedValue] = (user, primary);
            return user;
        }

        public void AddLogin(
            AuthUser user,
            ExternalIdentity identity,
            DateTimeOffset connectedAt,
            DateTimeOffset? lastUsedAt = null)
        {
            logins[(identity.Provider, identity.Subject)] = new ExternalLoginSnapshot(
                user.Id,
                identity.Provider,
                identity.Subject,
                identity.Email,
                connectedAt,
                lastUsedAt);
        }

        public ExternalLoginSnapshot GetLogin(ExternalIdentity identity) =>
            logins[(identity.Provider, identity.Subject)];

        public Task<ExternalLoginSnapshot?> FindLoginAsync(
            ExternalProvider provider,
            string subject,
            CancellationToken ct)
        {
            Operations.Add("find-login");
            logins.TryGetValue((provider, subject), out var login);
            return Task.FromResult(login);
        }

        public Task<AuthUser?> FindUserByEmailAsync(
            string normalizedEmail,
            CancellationToken ct)
        {
            Operations.Add("find-email");
            return Task.FromResult(
                usersByEmail.TryGetValue(normalizedEmail, out var entry)
                    ? entry.User
                    : null);
        }

        public Task<AuthUser> CreateUserAsync(
            ExternalIdentity identity,
            CancellationToken ct)
        {
            Operations.Add("create-user");
            CreatedIdentities.Add(identity);
            if (CreateFailures.TryDequeue(out var failure))
            {
                throw failure;
            }

            return Task.FromResult(new AuthUser(
                UserId.New(),
                identity.DisplayName ?? identity.Email.Value,
                identity.Email.Value,
                true,
                identity.ImageUrl?.AbsoluteUri,
                false));
        }

        public Task EnsureVerifiedEmailAsync(
            UserId userId,
            VerifiedEmail email,
            bool primary,
            CancellationToken ct)
        {
            Operations.Add("ensure-email");
            EnsuredEmails.Add((userId, email, primary));
            return Task.CompletedTask;
        }

        public Task AddLoginAsync(
            UserId userId,
            ExternalIdentity identity,
            DateTimeOffset connectedAt,
            bool usedForSignIn,
            CancellationToken ct)
        {
            Operations.Add("add-login");
            AddedLogins.Add((userId, identity, connectedAt, usedForSignIn));
            logins[(identity.Provider, identity.Subject)] = new ExternalLoginSnapshot(
                userId,
                identity.Provider,
                identity.Subject,
                identity.Email,
                connectedAt,
                usedForSignIn ? connectedAt : null);
            return Task.CompletedTask;
        }

        public Task UpdateLoginEmailAsync(
            UserId userId,
            ExternalIdentity identity,
            DateTimeOffset? usedAt,
            CancellationToken ct)
        {
            Operations.Add("update-login");
            UpdatedLogins.Add((userId, identity, usedAt));
            var previous = logins[(identity.Provider, identity.Subject)];
            logins[(identity.Provider, identity.Subject)] =
                previous with
                {
                    Email = identity.Email,
                    LastUsedAt = usedAt ?? previous.LastUsedAt
                };
            return Task.CompletedTask;
        }

        public Task UpdateLinkedProfileAsync(
            UserId userId,
            string? displayName,
            Uri? imageUrl,
            CancellationToken ct)
        {
            Operations.Add("update-profile");
            ProfileUpdates.Add((userId, displayName, imageUrl));
            return Task.CompletedTask;
        }
    }
}
