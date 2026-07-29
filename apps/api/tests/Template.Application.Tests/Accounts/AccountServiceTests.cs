using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication;
using Template.Domain.Accounts;
using Template.Domain.Authentication;

namespace Template.Application.Tests.Accounts;

public sealed class AccountServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly UserId UserId = new(Guid.Parse("01987712-9e00-7000-8000-000000000001"));
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly ExternalProvider[] ConfiguredProviders =
    [
        ExternalProvider.Google,
        ExternalProvider.GitHub,
        ExternalProvider.GitLab,
        ExternalProvider.Vk,
        ExternalProvider.Yandex
    ];

    [Fact]
    public async Task ProfileReturnsTheStoredAccountSnapshot()
    {
        var account = Account();
        var store = new FakeAccountStore(account);
        var service = new AccountService(store);

        var result = await service.GetAsync(UserId, Ct);

        Assert.Equal(account, result);
    }

    [Fact]
    public async Task DisplayNameIsTrimmedBeforeUpdate()
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);

        var result = await service.UpdateDisplayNameAsync(UserId, "  Ada Lovelace  ", Ct);

        Assert.Null(result.Failure);
        Assert.Equal("Ada Lovelace", result.Value!.User.Name);
        Assert.Equal("Ada Lovelace", Assert.Single(store.UpdatedDisplayNames));
    }

    [Fact]
    public async Task ProfileUpdateWhoseAccountWasConcurrentlyDeletedRequiresSession()
    {
        var store = new FakeAccountStore(Account())
        {
            ProfileMissingAfterValidation = true
        };
        var service = new AccountService(store);

        var result = await service.UpdateDisplayNameAsync(
            UserId,
            "Concurrent Delete",
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.SessionRequired, result.Failure);
        Assert.Equal(
            "Concurrent Delete",
            Assert.Single(store.UpdatedDisplayNames));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData(" A ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxy")]
    [InlineData("valid\u0000name")]
    public async Task DisplayNameOutsideTheExactPolicyIsRejected(string displayName)
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);

        var result = await service.UpdateDisplayNameAsync(UserId, displayName, Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.InvalidDisplayName, result.Failure);
        Assert.Empty(store.UpdatedDisplayNames);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public async Task DisplayNameAcceptsInclusiveLengthBounds(int length)
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);
        var displayName = new string('x', length);

        var result = await service.UpdateDisplayNameAsync(UserId, displayName, Ct);

        Assert.Null(result.Failure);
        Assert.Equal(displayName, Assert.Single(store.UpdatedDisplayNames));
    }

    [Fact]
    public async Task ConnectionsAreTheUnionOfConfiguredAndExistingProviders()
    {
        var store = new FakeAccountStore(Account());
        store.Connections.Add(Connection(ExternalProvider.GitHub));
        store.Connections.Add(Connection(ExternalProvider.Vk));
        var service = new AccountService(store);

        var result = await service.ListConnectionsAsync(
            UserId,
            [ExternalProvider.Google, ExternalProvider.GitHub],
            Ct);

        Assert.Collection(
            result,
            google =>
            {
                Assert.Equal(ExternalProvider.Google, google.Provider);
                Assert.True(google.Configured);
                Assert.Null(google.Email);
            },
            github =>
            {
                Assert.Equal(ExternalProvider.GitHub, github.Provider);
                Assert.True(github.Configured);
                Assert.NotNull(github.Email);
            },
            vk =>
            {
                Assert.Equal(ExternalProvider.Vk, vk.Provider);
                Assert.False(vk.Configured);
                Assert.NotNull(vk.Email);
            });
    }

    [Fact]
    public async Task DuplicateConfiguredProvidersDoNotDuplicateProjection()
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);

        var result = await service.ListConnectionsAsync(
            UserId,
            [ExternalProvider.Google, ExternalProvider.Google],
            Ct);

        Assert.Single(result);
    }

    [Fact]
    public async Task MissingConnectionUsesConnectionNotFoundFailure()
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionNotFound, result.Failure);
        Assert.Empty(store.DisconnectAttempts);
    }

    [Fact]
    public async Task CurrentAuthenticationConnectionCannotBeDisconnected()
    {
        var snapshot = DisconnectSnapshot(
            ExternalProvider.Google,
            configuredSurvivorCount: 2);
        var store = new FakeAccountStore(Account()) { DisconnectSnapshot = snapshot };
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.Google,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionRequired, result.Failure);
        Assert.Empty(store.DisconnectAttempts);
    }

    [Fact]
    public async Task ConnectionWithoutConfiguredSurvivorCannotBeDisconnected()
    {
        var snapshot = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 0);
        var store = new FakeAccountStore(Account()) { DisconnectSnapshot = snapshot };
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionRequired, result.Failure);
        Assert.Empty(store.DisconnectAttempts);
    }

    [Fact]
    public async Task ConfiguredCandidateCannotBeRemovedWhenStoredSurvivorsAreUnconfigured()
    {
        var snapshot = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 0);
        var store = new FakeAccountStore(Account()) { DisconnectSnapshot = snapshot };
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            configuredProviders: [ExternalProvider.GitHub],
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionRequired, result.Failure);
        Assert.Empty(store.DisconnectAttempts);
    }

    [Fact]
    public async Task CandidateCanBeRemovedWhenAConfiguredSurvivorRemains()
    {
        var snapshot = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 1);
        var store = new FakeAccountStore(Account()) { DisconnectSnapshot = snapshot };
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Vk,
            provider: ExternalProvider.GitHub,
            configuredProviders:
            [
                ExternalProvider.GitHub,
                ExternalProvider.Google
            ],
            Ct);

        Assert.Null(result.Failure);
        Assert.Equal(ExternalProvider.GitHub, result.Value!.Provider);
        Assert.Equal(snapshot, Assert.Single(store.DisconnectAttempts));
    }

    [Fact]
    public async Task AllowedDisconnectDelegatesApprovedSnapshotToAtomicStoreOperation()
    {
        var snapshot = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 2,
            emailIsPrimary: false);
        var store = new FakeAccountStore(Account()) { DisconnectSnapshot = snapshot };
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Failure);
        Assert.Equal(ExternalProvider.GitHub, result.Value!.Provider);
        Assert.Equal(snapshot, Assert.Single(store.DisconnectAttempts));
    }

    [Fact]
    public async Task StaleDisconnectSnapshotThatBecomesMissingMapsFreshDecision()
    {
        var initial = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 2);
        var store = new FakeAccountStore(Account());
        store.DisconnectSnapshotReads.Enqueue(initial);
        store.DisconnectSnapshotReads.Enqueue(null);
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionNotFound, result.Failure);
        Assert.Equal(2, store.DisconnectSnapshotRequests.Count);
        Assert.Single(store.DisconnectAttempts);
    }

    [Fact]
    public async Task StaleDisconnectSnapshotThatBecomesLastMapsFreshDecision()
    {
        var initial = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 2);
        var last = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 0);
        var store = new FakeAccountStore(Account());
        store.DisconnectSnapshotReads.Enqueue(initial);
        store.DisconnectSnapshotReads.Enqueue(last);
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionRequired, result.Failure);
        Assert.Equal(2, store.DisconnectSnapshotRequests.Count);
        Assert.Single(store.DisconnectAttempts);
    }

    [Fact]
    public async Task SecondStaleDisconnectConflictThatWasRemovedMapsFinalState()
    {
        var initial = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 2);
        var fresh = initial with { ConfiguredSurvivorCount = 1 };
        var store = new FakeAccountStore(Account());
        store.DisconnectSnapshotReads.Enqueue(initial);
        store.DisconnectSnapshotReads.Enqueue(fresh);
        store.DisconnectSnapshotReads.Enqueue(null);
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionNotFound, result.Failure);
        Assert.Equal(3, store.DisconnectSnapshotRequests.Count);
        Assert.Equal([initial, fresh], store.DisconnectAttempts);
    }

    [Fact]
    public async Task SecondStaleDisconnectConflictThatBecameUnsafeMapsFinalState()
    {
        var initial = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 2);
        var fresh = initial with { ConfiguredSurvivorCount = 1 };
        var last = initial with { ConfiguredSurvivorCount = 0 };
        var store = new FakeAccountStore(Account());
        store.DisconnectSnapshotReads.Enqueue(initial);
        store.DisconnectSnapshotReads.Enqueue(fresh);
        store.DisconnectSnapshotReads.Enqueue(last);
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConnectionRequired, result.Failure);
        Assert.Equal(3, store.DisconnectSnapshotRequests.Count);
        Assert.Equal([initial, fresh], store.DisconnectAttempts);
    }

    [Fact]
    public async Task SecondStaleDisconnectConflictStillSafeReturnsAccurateContention()
    {
        var initial = DisconnectSnapshot(
            ExternalProvider.GitHub,
            configuredSurvivorCount: 2);
        var fresh = initial with { ConfiguredSurvivorCount = 1 };
        var terminal = initial with { ConfiguredSurvivorCount = 3 };
        var store = new FakeAccountStore(Account());
        store.DisconnectSnapshotReads.Enqueue(initial);
        store.DisconnectSnapshotReads.Enqueue(fresh);
        store.DisconnectSnapshotReads.Enqueue(terminal);
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        store.DisconnectFailures.Enqueue(new AccountConcurrencyException());
        var service = new AccountService(store);

        var result = await service.DisconnectAsync(
            UserId,
            currentAuthenticationProvider: ExternalProvider.Google,
            provider: ExternalProvider.GitHub,
            ConfiguredProviders,
            Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConcurrencyConflict, result.Failure);
        Assert.Equal(3, store.DisconnectSnapshotRequests.Count);
        Assert.Equal([initial, fresh], store.DisconnectAttempts);
    }

    [Theory]
    [InlineData("OWNER@EXAMPLE.TEST")]
    [InlineData("  owner@example.test  ")]
    [InlineData("  OwNeR@Example.Test  ")]
    public async Task DeleteConfirmationUsesTrimmedNormalizedPrimaryEmail(
        string confirmation)
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);

        var result = await service.DeleteAsync(UserId, confirmation, Ct);

        Assert.Null(result.Failure);
        Assert.Equal(UserId, result.Value!.UserId);
        Assert.Equal(UserId, Assert.Single(store.DeletedUserIds));
        Assert.True(store.DeleteCompleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("other@example.test")]
    [InlineData("owner@example.test\u0000")]
    public async Task DeleteConfirmationMismatchDoesNotDelete(string confirmation)
    {
        var store = new FakeAccountStore(Account());
        var service = new AccountService(store);

        var result = await service.DeleteAsync(UserId, confirmation, Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.ConfirmationMismatch, result.Failure);
        Assert.Empty(store.DeletedUserIds);
    }

    private static AccountSnapshot Account() =>
        new(
            new AuthUser(
                UserId,
                "Owner",
                "owner@example.test",
                EmailVerified: true,
                Image: null,
                IsLocalAutomation: false),
            VerifiedEmail.Create("owner@example.test"),
            [
                new AccountEmail(
                    VerifiedEmail.Create("owner@example.test"),
                    IsPrimary: true,
                    [ExternalProvider.Google])
            ],
            CreatedAt);

    private static AccountConnection Connection(ExternalProvider provider) =>
        new(
            provider,
            Configured: false,
            VerifiedEmail.Create($"{provider.Value}@example.test"),
            CreatedAt,
            CreatedAt.AddHours(1));

    private static DisconnectSnapshot DisconnectSnapshot(
        ExternalProvider provider,
        int configuredSurvivorCount,
        bool emailIsPrimary = false) =>
        new(
            UserId,
            provider,
            VerifiedEmail.Create($"{provider.Value}@example.test"),
            emailIsPrimary,
            configuredSurvivorCount);

    private sealed class FakeAccountStore(AccountSnapshot account) : IAccountStore
    {
        public List<AccountConnection> Connections { get; } = [];
        public List<string> UpdatedDisplayNames { get; } = [];
        public List<DisconnectSnapshot> DisconnectAttempts { get; } = [];
        public List<(UserId UserId, ExternalProvider Provider)>
            DisconnectSnapshotRequests
        { get; } = [];
        public Queue<DisconnectSnapshot?> DisconnectSnapshotReads { get; } = [];
        public Queue<AccountConcurrencyException> DisconnectFailures { get; } = [];
        public List<UserId> DeletedUserIds { get; } = [];
        public DisconnectSnapshot? DisconnectSnapshot { get; init; }
        public bool ProfileMissingAfterValidation { get; init; }
        public bool DeleteCompleted { get; private set; }

        public Task<AccountSnapshot?> GetAsync(UserId userId, CancellationToken ct) =>
            Task.FromResult<AccountSnapshot?>(userId == account.User.Id ? account : null);

        public Task<AccountSnapshot?> UpdateDisplayNameAsync(
            UserId userId,
            string displayName,
            CancellationToken ct)
        {
            UpdatedDisplayNames.Add(displayName);
            if (ProfileMissingAfterValidation)
            {
                return Task.FromResult<AccountSnapshot?>(null);
            }

            account = account with { User = account.User with { Name = displayName } };
            return Task.FromResult<AccountSnapshot?>(account);
        }

        public Task<IReadOnlyList<AccountConnection>> ListConnectionsAsync(
            UserId userId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AccountConnection>>(Connections);

        public Task<DisconnectSnapshot?> GetDisconnectSnapshotAsync(
            UserId userId,
            ExternalProvider provider,
            IReadOnlyCollection<ExternalProvider> configuredProviders,
            CancellationToken ct)
        {
            DisconnectSnapshotRequests.Add((userId, provider));
            var snapshot = DisconnectSnapshotReads.Count > 0
                ? DisconnectSnapshotReads.Dequeue()
                : DisconnectSnapshot?.Provider == provider
                    ? DisconnectSnapshot
                    : null;
            return Task.FromResult(snapshot);
        }

        public Task DisconnectAsync(
            DisconnectSnapshot snapshot,
            IReadOnlyCollection<ExternalProvider> configuredProviders,
            CancellationToken ct)
        {
            DisconnectAttempts.Add(snapshot);
            if (DisconnectFailures.TryDequeue(out var failure))
            {
                throw failure;
            }

            return Task.CompletedTask;
        }

        public async Task DeleteAsync(UserId userId, CancellationToken ct)
        {
            DeletedUserIds.Add(userId);
            await Task.Yield();
            DeleteCompleted = true;
        }
    }
}
