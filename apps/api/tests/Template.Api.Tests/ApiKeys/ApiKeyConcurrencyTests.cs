using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;
using Template.Api.Tests.Infrastructure;
using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;

namespace Template.Api.Tests.ApiKeys;

public sealed class ApiKeyConcurrencyTests(PostgreSqlContainerFixture postgres)
{
    [Fact]
    public async Task Exactly_the_configured_number_of_concurrent_presentations_succeed_in_one_window()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("quota@keys.test");
        var hash = Hash(1);
        await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), hash, "user_abcdefghijk", rateLimitMax: 3);

        fixture.CoordinateAuthenticationStarts(participants: 8);
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => fixture.AuthenticateInNewScopeAsync(hash))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(8, fixture.CoordinatedAuthenticationArrivals);
        Assert.Equal(3, results.Count(result => result.Outcome == ApiKeyAuthenticationOutcome.Succeeded));
        Assert.Equal(5, results.Count(result => result.Outcome == ApiKeyAuthenticationOutcome.RateLimited));
        Assert.All(results.Where(result => result.Outcome == ApiKeyAuthenticationOutcome.RateLimited), result => Assert.True(result.RetryAfter > TimeSpan.Zero));
        Assert.Equal(3, await fixture.RequestCountAsync(hash));
    }

    [Fact]
    public async Task First_presentation_after_window_expiry_resets_persisted_window_to_count_one()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("reset@keys.test");
        var hash = Hash(2);
        await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), hash, "user_abcdefghijk", rateLimitMax: 1);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await fixture.Store.AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken)).Outcome);
        fixture.SetTime(ApiKeyStoreFixture.Now.AddSeconds(30));
        Assert.Equal(ApiKeyAuthenticationOutcome.RateLimited, (await fixture.Store.AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken)).Outcome);

        var resetAt = ApiKeyStoreFixture.Now.AddMinutes(1);
        fixture.SetTime(resetAt);
        var reset = await fixture.Store.AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, reset.Outcome);
        var state = await fixture.QuotaStateAsync(hash);
        Assert.Equal((resetAt, 1), state);
    }

    [Fact]
    public async Task Waiting_presentation_samples_time_after_a_newer_window_commits()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("ordered-window@keys.test");
        var hash = Hash(25);
        await fixture.CreateKeyAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            hash,
            "user_abcdefghijk",
            rateLimitMax: 1);
        Assert.Equal(
            ApiKeyAuthenticationOutcome.Succeeded,
            (await fixture.Store.AuthenticateAndConsumeAsync(
                hash,
                TestContext.Current.CancellationToken)).Outcome);
        var staleCallerTime = ApiKeyStoreFixture.Now.AddSeconds(30);
        var committedTime = ApiKeyStoreFixture.Now.AddMinutes(1).AddSeconds(1);
        fixture.SetTime(staleCallerTime);
        fixture.PauseNextAuthenticationBeforeKeyLock();

        var waiting = fixture.AuthenticateInNewScopeAsync(hash);
        await fixture.WaitForAuthenticationBeforeKeyLockAsync();
        fixture.SetTime(committedTime);
        var committed = await fixture.AuthenticateInNewScopeAsync(hash);
        fixture.ReleaseAuthenticationBeforeKeyLock();
        var reordered = await waiting;

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, committed.Outcome);
        Assert.Equal(ApiKeyAuthenticationOutcome.RateLimited, reordered.Outcome);
        Assert.Equal(TimeSpan.FromMinutes(1), reordered.RetryAfter);
        var state = await fixture.TemporalStateAsync(hash);
        Assert.Equal(committedTime, state.WindowStartedAt);
        Assert.Equal(committedTime, state.LastRequestAt);
        Assert.Equal(1, state.RequestCount);
    }

    [Fact]
    public async Task Reordered_successful_presentation_never_regresses_last_request_time()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("ordered-last-use@keys.test");
        var hash = Hash(26);
        await fixture.CreateKeyAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            hash,
            "user_abcdefghijk",
            rateLimitMax: 2);
        Assert.Equal(
            ApiKeyAuthenticationOutcome.Succeeded,
            (await fixture.Store.AuthenticateAndConsumeAsync(
                hash,
                TestContext.Current.CancellationToken)).Outcome);
        var staleCallerTime = ApiKeyStoreFixture.Now.AddSeconds(30);
        var committedTime = ApiKeyStoreFixture.Now.AddMinutes(1).AddSeconds(1);
        fixture.SetTime(staleCallerTime);
        fixture.PauseNextAuthenticationBeforeKeyLock();

        var waiting = fixture.AuthenticateInNewScopeAsync(hash);
        await fixture.WaitForAuthenticationBeforeKeyLockAsync();
        fixture.SetTime(committedTime);
        var committed = await fixture.AuthenticateInNewScopeAsync(hash);
        fixture.ReleaseAuthenticationBeforeKeyLock();
        var reordered = await waiting;

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, committed.Outcome);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, reordered.Outcome);
        var state = await fixture.TemporalStateAsync(hash);
        Assert.Equal(committedTime, state.WindowStartedAt);
        Assert.Equal(committedTime, state.LastRequestAt);
        Assert.Equal(2, state.RequestCount);
    }

    [Fact]
    public async Task Authentication_resamples_validity_time_on_a_fresh_transaction_attempt()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("retry-time@keys.test");
        var hash = Hash(27);
        var expiresAt = ApiKeyStoreFixture.Now.AddMinutes(1);
        await fixture.CreateKeyAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            hash,
            "user_abcdefghijk",
            expiresAt: expiresAt);
        fixture.SetTime(ApiKeyStoreFixture.Now);
        fixture.FailNextAuthenticationAttempts(
            1,
            expiresAt.AddSeconds(1));

        var result = await fixture.AuthenticateInNewScopeAsync(hash);

        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, result.Outcome);
        Assert.Equal(2, fixture.AuthenticationAttempts);
        Assert.Equal(2, fixture.AuthenticationTransactionCount);
    }

    [Fact]
    public async Task Valid_presentation_consumes_before_a_later_scope_denial()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("scope@keys.test");
        var hash = Hash(3);
        await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), hash, "user_abcdefghijk", rateLimitMax: 1);

        var valid = await fixture.Store.AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, valid.Outcome);
        Assert.DoesNotContain(ApiKeyScopes.TeamRead, valid.Principal!.Scopes);

        var afterApplicationScopeDenial = await fixture.Store.AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken);
        Assert.Equal(ApiKeyAuthenticationOutcome.RateLimited, afterApplicationScopeDenial.Outcome);
    }

    [Fact]
    public async Task Disabled_expired_revoked_and_unknown_hashes_are_indistinguishable()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("invalid@keys.test");
        var disabled = Hash(4);
        var expired = Hash(5);
        var revoked = Hash(6);
        var disabledKey = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), disabled, "user_abcdefghijk")).Value!;
        var expiredKey = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), expired, "user_bcdefghijkl")).Value!;
        var revokedKey = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), revoked, "user_cdefghijklm")).Value!;
        await fixture.SetTerminalStateAsync(disabledKey.Id, enabled: false, expiresAt: null, revokedAt: null);
        await fixture.SetTerminalStateAsync(expiredKey.Id, enabled: true, expiresAt: ApiKeyStoreFixture.Now, revokedAt: null);
        await fixture.SetTerminalStateAsync(revokedKey.Id, enabled: true, expiresAt: null, revokedAt: ApiKeyStoreFixture.Now);

        var outcomes = await Task.WhenAll(new[] { disabled, expired, revoked, Hash(99) }.Select(fixture.AuthenticateInNewScopeAsync));

        Assert.All(outcomes, result =>
        {
            Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, result.Outcome);
            Assert.Null(result.Principal);
            Assert.Null(result.RetryAfter);
        });
    }

    [Theory]
    [InlineData(ApiKeyMutationKind.Rotate)]
    [InlineData(ApiKeyMutationKind.Revoke)]
    public async Task Presentation_that_holds_the_key_lock_commits_before_rotate_or_revoke(
        ApiKeyMutationKind mutationKind)
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync($"before-{mutationKind}@keys.test");
        var oldHash = Hash(mutationKind == ApiKeyMutationKind.Rotate ? 7 : 8);
        var newHash = Hash(9);
        var key = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), oldHash, "user_abcdefghijk")).Value!;
        fixture.HoldTransactionAfterKeyLock(ApiKeyTransactionKind.Authentication, ApiKeyTransactionKind.Management);

        var use = fixture.AuthenticateInNewScopeAsync(oldHash);
        await fixture.WaitForHeldKeyLockAsync();
        var mutation = fixture.MutateInNewScopeAsync(mutationKind, actor, key.Id, newHash);
        await fixture.WaitForCompetingKeyLockStartAsync();
        fixture.ReleaseHeldTransaction();
        await Task.WhenAll(use, mutation);
        var useResult = await use;
        var mutationResult = await mutation;

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, useResult.Outcome);
        Assert.True(mutationResult.Succeeded);
        var row = await fixture.ReadKeyAsync(key.Id);
        Assert.Equal(ApiKeyStoreFixture.Now, row.LastRequestAt);
        Assert.Equal(mutationKind == ApiKeyMutationKind.Rotate ? 0 : 1, row.RequestCount);
        Assert.Equal(mutationKind == ApiKeyMutationKind.Revoke, row.RevokedAt is not null);
        if (mutationKind == ApiKeyMutationKind.Rotate)
        {
            Assert.Equal(newHash, row.KeyHash);
        }
    }

    [Theory]
    [InlineData(ApiKeyMutationKind.Rotate)]
    [InlineData(ApiKeyMutationKind.Revoke)]
    public async Task Rotate_or_revoke_that_holds_the_key_lock_commits_before_presentation(
        ApiKeyMutationKind mutationKind)
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync($"after-{mutationKind}@keys.test");
        var oldHash = Hash(mutationKind == ApiKeyMutationKind.Rotate ? 11 : 12);
        var newHash = Hash(13);
        var key = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), oldHash, "user_abcdefghijk")).Value!;
        fixture.HoldTransactionAfterKeyLock(ApiKeyTransactionKind.Management, ApiKeyTransactionKind.Authentication);

        var mutation = fixture.MutateInNewScopeAsync(mutationKind, actor, key.Id, newHash);
        await fixture.WaitForHeldKeyLockAsync();
        var use = fixture.AuthenticateInNewScopeAsync(oldHash);
        await fixture.WaitForCompetingKeyLockStartAsync();
        fixture.ReleaseHeldTransaction();
        await Task.WhenAll(mutation, use);
        var mutationResult = await mutation;
        var useResult = await use;

        Assert.True(mutationResult.Succeeded);
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, useResult.Outcome);
        var row = await fixture.ReadKeyAsync(key.Id);
        Assert.Null(row.LastRequestAt);
        Assert.Equal(0, row.RequestCount);
        Assert.Equal(mutationKind == ApiKeyMutationKind.Revoke, row.RevokedAt is not null);
        if (mutationKind == ApiKeyMutationKind.Rotate)
        {
            Assert.Equal(newHash, row.KeyHash);
        }
    }

    [Fact]
    public async Task Authentication_retries_three_fresh_transactions_then_preserves_the_database_failure()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("retry@keys.test");
        var hash = Hash(10);
        await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), hash, "user_abcdefghijk");
        fixture.FailNextAuthenticationAttempts(3);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.AuthenticateAndConsumeAsync(hash, TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.SerializationFailure,
            Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        Assert.Equal(3, fixture.AuthenticationAttempts);
        Assert.Equal(3, fixture.AuthenticationTransactionCount);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public async Task Management_retries_40001_and_40P01_in_fresh_transactions(
        string sqlState)
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync($"management-{sqlState}@keys.test");
        fixture.FailNextPersonalManagementInserts(sqlState, count: 1);

        var result = await fixture.CreateKeyInNewScopeAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            Hash(sqlState == PostgresErrorCodes.SerializationFailure ? 20 : 21),
            "user_abcdefghijk");

        Assert.True(result.Succeeded);
        Assert.Equal(2, fixture.PersonalManagementLockAttempts);
        Assert.Equal(2, fixture.ManagementTransactionCount);
    }

    [Fact]
    public async Task Unique_hash_collision_is_classified_without_transaction_retry()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("collision@keys.test");
        var hash = Hash(22);
        await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), hash, "user_abcdefghijk");
        fixture.ObservePersonalManagement();

        var collision = await fixture.CreateKeyInNewScopeAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            hash,
            "user_bcdefghijkl");

        Assert.Equal(ApiKeyFailure.ConcurrencyConflict, collision.Failure);
        Assert.Equal(1, fixture.PersonalManagementLockAttempts);
        Assert.Equal(1, fixture.ManagementTransactionCount);
    }

    [Fact]
    public async Task Permission_denial_is_not_retried()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var owner = await fixture.CreateUserAsync("permission-owner@keys.test");
        var member = await fixture.CreateUserAsync("permission-member@keys.test");
        var organization = await fixture.CreateOrganizationAsync(owner, Template.Domain.Organizations.OrganizationRole.Owner);
        await fixture.AddMemberAsync(organization, member, Template.Domain.Organizations.OrganizationRole.Member);
        fixture.ObserveOrganizationManagement();

        var denied = await fixture.CreateKeyInNewScopeAsync(
            member,
            new(ApiKeyOwnerKind.Organization, null, organization),
            Hash(23),
            "org_abcdefghijkl");

        Assert.Equal(ApiKeyFailure.PermissionDenied, denied.Failure);
        Assert.Equal(1, fixture.OrganizationManagementLockAttempts);
        Assert.Equal(1, fixture.MembershipAuthorizationAttempts);
        Assert.Equal(1, fixture.ManagementTransactionCount);
    }

    [Fact]
    public async Task Organization_role_is_reauthorized_on_the_fresh_retry()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("role-retry@keys.test");
        var organization = await fixture.CreateOrganizationAsync(actor, Template.Domain.Organizations.OrganizationRole.Owner);
        fixture.FailFirstOrganizationInsertAndPauseSecondAttempt(
            PostgresErrorCodes.SerializationFailure);

        var create = fixture.CreateKeyInNewScopeAsync(
            actor,
            new(ApiKeyOwnerKind.Organization, null, organization),
            Hash(24),
            "org_abcdefghijkl");
        await fixture.WaitForSecondOrganizationAttemptAsync();
        await fixture.SetRoleAsync(
            organization,
            actor,
            Template.Domain.Organizations.OrganizationRole.Member);
        fixture.ReleaseSecondOrganizationAttempt();
        var result = await create;

        Assert.Equal(ApiKeyFailure.PermissionDenied, result.Failure);
        Assert.Equal(2, fixture.OrganizationManagementLockAttempts);
        Assert.Equal(2, fixture.MembershipAuthorizationAttempts);
        Assert.Equal(2, fixture.ManagementTransactionCount);
        Assert.Equal(0, await fixture.CountKeysAsync());
    }

    [Fact]
    public async Task Create_samples_time_after_owner_lock_on_every_retry()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("create-time@keys.test");
        var firstAttemptTime = ApiKeyStoreFixture.Now.AddMinutes(5);
        var committedTime = firstAttemptTime.AddMinutes(2);
        fixture.SetTime(firstAttemptTime);
        fixture.FailNextPersonalManagementInserts(
            PostgresErrorCodes.SerializationFailure,
            count: 1,
            advanceTimeTo: committedTime);

        var result = await fixture.CreateKeyInNewScopeAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            Hash(28),
            "user_abcdefghijk",
            firstAttemptTime.AddDays(30),
            firstAttemptTime);

        Assert.True(result.Succeeded);
        Assert.Equal(committedTime, result.Value!.CreatedAt);
        Assert.Equal(committedTime, result.Value.UpdatedAt);
        Assert.Equal(committedTime.AddDays(30), result.Value.ExpiresAt);
        Assert.Equal(2, fixture.PersonalManagementLockAttempts);
        Assert.Equal(2, fixture.ManagementTransactionCount);
    }

    [Fact]
    public async Task Update_converts_relative_expiry_after_owner_and_key_locks()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("update-time@keys.test");
        var key = (await fixture.CreateKeyAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            Hash(29),
            "user_abcdefghijk")).Value!;
        var callTime = ApiKeyStoreFixture.Now.AddMinutes(10);
        var lockTime = callTime.AddMinutes(3);
        fixture.SetTime(callTime);
        fixture.HoldTransactionAfterKeyLock(
            ApiKeyTransactionKind.Management,
            ApiKeyTransactionKind.Authentication);

        var update = fixture.UpdateExpirationInNewScopeAsync(
            actor,
            key.Id,
            TimeSpan.FromDays(30));
        await fixture.WaitForHeldKeyLockAsync();
        fixture.SetTime(lockTime);
        fixture.ReleaseHeldTransaction();
        var result = await update;

        Assert.True(result.Succeeded);
        Assert.Equal(lockTime.AddDays(30), result.Value!.ExpiresAt);
        Assert.Equal(lockTime, result.Value.UpdatedAt);
    }

    [Fact]
    public async Task Rotation_waiting_for_a_use_never_commits_before_last_request()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("rotate-time@keys.test");
        var oldHash = Hash(30);
        var key = (await fixture.CreateKeyAsync(
            actor,
            new(ApiKeyOwnerKind.User, actor, null),
            oldHash,
            "user_abcdefghijk")).Value!;
        var rotationCallTime = ApiKeyStoreFixture.Now.AddMinutes(1);
        var useTime = rotationCallTime.AddMinutes(2);
        fixture.SetTime(rotationCallTime);
        fixture.HoldTransactionAfterKeyLock(
            ApiKeyTransactionKind.Authentication,
            ApiKeyTransactionKind.Management);

        var use = fixture.AuthenticateInNewScopeAsync(oldHash);
        await fixture.WaitForHeldKeyLockAsync();
        var rotate = fixture.RotateInNewScopeAsync(
            actor,
            key.Id,
            Hash(31),
            "user_bcdefghijkl");
        await fixture.WaitForCompetingKeyLockStartAsync();
        fixture.SetTime(useTime);
        fixture.ReleaseHeldTransaction();
        await Task.WhenAll(use, rotate);

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await use).Outcome);
        Assert.True((await rotate).Succeeded);
        var row = await fixture.ReadKeyAsync(key.Id);
        Assert.Equal(useTime, row.LastRequestAt);
        Assert.Equal(useTime, row.RotatedAt);
        Assert.Equal(useTime, row.UpdatedAt);
    }

    private static byte[] Hash(int value) => Enumerable.Repeat((byte)value, 32).ToArray();
}

public enum ApiKeyMutationKind { Rotate, Revoke }
public enum ApiKeyTransactionKind { Authentication, Management }

internal sealed class ApiKeyTransactionBarrier : DbCommandInterceptor
{
    private int _authenticationParticipants;
    private int _authenticationArrivals;
    private TaskCompletionSource _authenticationRelease = NewSignal();
    private ApiKeyTransactionKind? _holder;
    private ApiKeyTransactionKind? _contender;
    private TaskCompletionSource _holderReached = NewSignal();
    private TaskCompletionSource _contenderReached = NewSignal();
    private TaskCompletionSource _holderRelease = NewSignal();
    private int _pauseAuthenticationBeforeKeyLock;
    private TaskCompletionSource _authenticationBeforeKeyLock = NewSignal();
    private TaskCompletionSource _authenticationBeforeKeyLockRelease = NewSignal();

    internal int AuthenticationArrivals => Volatile.Read(ref _authenticationArrivals);

    internal void CoordinateAuthenticationStarts(int participants)
    {
        _authenticationParticipants = participants;
        Interlocked.Exchange(ref _authenticationArrivals, 0);
        _authenticationRelease = NewSignal();
    }

    internal void HoldAfterKeyLock(ApiKeyTransactionKind holder, ApiKeyTransactionKind contender)
    {
        _holder = holder;
        _contender = contender;
        _holderReached = NewSignal();
        _contenderReached = NewSignal();
        _holderRelease = NewSignal();
    }

    internal void PauseNextAuthenticationBeforeKeyLock()
    {
        Volatile.Write(ref _pauseAuthenticationBeforeKeyLock, 1);
        _authenticationBeforeKeyLock = NewSignal();
        _authenticationBeforeKeyLockRelease = NewSignal();
    }

    internal Task WaitForAuthenticationBeforeKeyLockAsync(
        CancellationToken cancellationToken) =>
        _authenticationBeforeKeyLock.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

    internal void ReleaseAuthenticationBeforeKeyLock() =>
        _authenticationBeforeKeyLockRelease.TrySetResult();

    internal Task WaitForHolderAsync(CancellationToken cancellationToken) =>
        _holderReached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    internal Task WaitForContenderAsync(CancellationToken cancellationToken) =>
        _contenderReached.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    internal void ReleaseHolder() => _holderRelease.TrySetResult();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var kind = Classify(command.CommandText);
        if (kind == ApiKeyTransactionKind.Authentication &&
            Interlocked.CompareExchange(
                ref _pauseAuthenticationBeforeKeyLock,
                0,
                1) == 1)
        {
            _authenticationBeforeKeyLock.TrySetResult();
            await _authenticationBeforeKeyLockRelease.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                cancellationToken);
        }
        if (_authenticationParticipants > 0 && kind == ApiKeyTransactionKind.Authentication)
        {
            var arrivals = Interlocked.Increment(ref _authenticationArrivals);
            if (arrivals == _authenticationParticipants)
            {
                _authenticationParticipants = 0;
                _authenticationRelease.TrySetResult();
            }
            await _authenticationRelease.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }
        if (_contender is not null && kind == _contender)
        {
            _contenderReached.TrySetResult();
        }
        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (_holder is not null && Classify(command.CommandText) == _holder)
        {
            _holderReached.TrySetResult();
            await _holderRelease.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            _holder = null;
            _contender = null;
        }
        return result;
    }

    private static ApiKeyTransactionKind? Classify(string commandText)
    {
        if (!commandText.Contains("FROM auth.api_keys", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return commandText.Contains("WHERE key_hash", StringComparison.OrdinalIgnoreCase)
            ? ApiKeyTransactionKind.Authentication
            : commandText.Contains("WHERE id", StringComparison.OrdinalIgnoreCase)
                ? ApiKeyTransactionKind.Management
                : null;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ApiKeyFailureInterceptor : DbCommandInterceptor
{
    private readonly MutableApiKeyTimeProvider _timeProvider;
    private readonly HashSet<Guid> _transactions = [];
    private int _remainingAuthenticationFailures;
    private int _observeAuthenticationAttempts;
    private DateTimeOffset? _advanceAuthenticationFailureTo;
    private int _authenticationAttempts;
    private int _managementEnabled;
    private string? _managementSqlState;
    private int _remainingManagementFailures;
    private int _observePersonal;
    private int _observeOrganization;
    private int _personalLocks;
    private int _organizationLocks;
    private int _membershipLocks;
    private int _pauseSecondOrganizationAttempt;
    private DateTimeOffset? _advanceManagementFailureTo;
    private TaskCompletionSource _secondOrganizationAttempt = NewSignal();
    private TaskCompletionSource _releaseSecondOrganizationAttempt = NewSignal();

    public ApiKeyFailureInterceptor(MutableApiKeyTimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    internal int AuthenticationAttempts => Volatile.Read(ref _authenticationAttempts);
    internal int AuthenticationTransactionCount
    {
        get { lock (_transactions) return _transactions.Count; }
    }
    internal int PersonalManagementLockAttempts => Volatile.Read(ref _personalLocks);
    internal int OrganizationManagementLockAttempts => Volatile.Read(ref _organizationLocks);
    internal int MembershipAuthorizationAttempts => Volatile.Read(ref _membershipLocks);
    internal int ManagementTransactionCount
    {
        get { lock (_transactions) return _transactions.Count; }
    }

    internal void FailNextAuthenticationAttempts(
        int count,
        DateTimeOffset? advanceTimeTo = null)
    {
        ResetManagement();
        Volatile.Write(ref _remainingAuthenticationFailures, count);
        Volatile.Write(ref _observeAuthenticationAttempts, 1);
        _advanceAuthenticationFailureTo = advanceTimeTo;
        Interlocked.Exchange(ref _authenticationAttempts, 0);
        lock (_transactions) _transactions.Clear();
    }

    internal void FailNextPersonalManagementInserts(
        string sqlState,
        int count,
        DateTimeOffset? advanceTimeTo = null)
    {
        ResetManagement();
        _managementSqlState = sqlState;
        _remainingManagementFailures = count;
        _advanceManagementFailureTo = advanceTimeTo;
        Volatile.Write(ref _observePersonal, 1);
        Volatile.Write(ref _managementEnabled, 1);
    }

    internal void ObservePersonalManagement()
    {
        ResetManagement();
        Volatile.Write(ref _observePersonal, 1);
        Volatile.Write(ref _managementEnabled, 1);
    }

    internal void ObserveOrganizationManagement()
    {
        ResetManagement();
        Volatile.Write(ref _observeOrganization, 1);
        Volatile.Write(ref _managementEnabled, 1);
    }

    internal void FailFirstOrganizationInsertAndPauseSecondAttempt(string sqlState)
    {
        ResetManagement();
        _managementSqlState = sqlState;
        _remainingManagementFailures = 1;
        Volatile.Write(ref _observeOrganization, 1);
        Volatile.Write(ref _pauseSecondOrganizationAttempt, 1);
        Volatile.Write(ref _managementEnabled, 1);
    }

    internal Task WaitForSecondOrganizationAttemptAsync(CancellationToken cancellationToken) =>
        _secondOrganizationAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    internal void ReleaseSecondOrganizationAttempt() =>
        _releaseSecondOrganizationAttempt.TrySetResult();

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _observeAuthenticationAttempts) == 0 ||
            !command.CommandText.Contains("WHERE key_hash", StringComparison.OrdinalIgnoreCase) ||
            !command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            await ObserveManagementAsync(command, eventData, cancellationToken);
            return result;
        }

        Interlocked.Increment(ref _authenticationAttempts);
        var transactionId = eventData.Context?.Database.CurrentTransaction?.TransactionId;
        if (transactionId is not null)
        {
            lock (_transactions) _transactions.Add(transactionId.Value);
        }
        if (Volatile.Read(ref _remainingAuthenticationFailures) <= 0)
        {
            return result;
        }

        Interlocked.Decrement(ref _remainingAuthenticationFailures);
        if (_advanceAuthenticationFailureTo is { } authenticationTime)
        {
            _timeProvider.SetUtcNow(authenticationTime);
            _advanceAuthenticationFailureTo = null;
        }
        throw new PostgresException(
            "deterministic serialization failure",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.SerializationFailure);
    }

    private async Task ObserveManagementAsync(
        DbCommand command,
        CommandEventData eventData,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _managementEnabled) == 0)
        {
            return;
        }

        if (Volatile.Read(ref _observePersonal) == 1 &&
            IsLock(command.CommandText, "FROM auth.users"))
        {
            Interlocked.Increment(ref _personalLocks);
            ObserveTransaction(eventData);
        }
        if (Volatile.Read(ref _observeOrganization) == 1 &&
            IsLock(command.CommandText, "FROM organizations.organizations"))
        {
            var attempt = Interlocked.Increment(ref _organizationLocks);
            ObserveTransaction(eventData);
            if (attempt == 2 && Volatile.Read(ref _pauseSecondOrganizationAttempt) == 1)
            {
                _secondOrganizationAttempt.TrySetResult();
                await _releaseSecondOrganizationAttempt.Task.WaitAsync(
                    TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
        if (Volatile.Read(ref _observeOrganization) == 1 &&
            IsLock(command.CommandText, "FROM organizations.members"))
        {
            Interlocked.Increment(ref _membershipLocks);
        }
        if (Volatile.Read(ref _remainingManagementFailures) > 0 &&
            command.CommandText.Contains("INSERT INTO auth.api_keys", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Decrement(ref _remainingManagementFailures);
            if (_advanceManagementFailureTo is { } managementTime)
            {
                _timeProvider.SetUtcNow(managementTime);
                _advanceManagementFailureTo = null;
            }
            throw new PostgresException(
                "deterministic management concurrency failure",
                "ERROR",
                "ERROR",
                _managementSqlState!);
        }
    }

    private void ObserveTransaction(CommandEventData eventData)
    {
        var transactionId = eventData.Context?.Database.CurrentTransaction?.TransactionId;
        if (transactionId is not null)
        {
            lock (_transactions) _transactions.Add(transactionId.Value);
        }
    }

    private void ResetManagement()
    {
        Volatile.Write(ref _observeAuthenticationAttempts, 0);
        Volatile.Write(ref _remainingAuthenticationFailures, 0);
        _advanceAuthenticationFailureTo = null;
        Volatile.Write(ref _managementEnabled, 0);
        _managementSqlState = null;
        _advanceManagementFailureTo = null;
        Volatile.Write(ref _remainingManagementFailures, 0);
        Volatile.Write(ref _observePersonal, 0);
        Volatile.Write(ref _observeOrganization, 0);
        Interlocked.Exchange(ref _personalLocks, 0);
        Interlocked.Exchange(ref _organizationLocks, 0);
        Interlocked.Exchange(ref _membershipLocks, 0);
        Volatile.Write(ref _pauseSecondOrganizationAttempt, 0);
        _secondOrganizationAttempt = NewSignal();
        _releaseSecondOrganizationAttempt = NewSignal();
        lock (_transactions) _transactions.Clear();
    }

    private static bool IsLock(string commandText, string table) =>
        commandText.Contains(table, StringComparison.OrdinalIgnoreCase) &&
        commandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
