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

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            await gate.Task;
            return await fixture.AuthenticateInNewScopeAsync(hash, ApiKeyStoreFixture.Now);
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(attempts);

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
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, (await fixture.Store.AuthenticateAndConsumeAsync(hash, ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(ApiKeyAuthenticationOutcome.RateLimited, (await fixture.Store.AuthenticateAndConsumeAsync(hash, ApiKeyStoreFixture.Now.AddSeconds(30), TestContext.Current.CancellationToken)).Outcome);

        var resetAt = ApiKeyStoreFixture.Now.AddMinutes(1);
        var reset = await fixture.Store.AuthenticateAndConsumeAsync(hash, resetAt, TestContext.Current.CancellationToken);

        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, reset.Outcome);
        var state = await fixture.QuotaStateAsync(hash);
        Assert.Equal((resetAt, 1), state);
    }

    [Fact]
    public async Task Valid_presentation_consumes_before_a_later_scope_denial()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("scope@keys.test");
        var hash = Hash(3);
        await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), hash, "user_abcdefghijk", rateLimitMax: 1);

        var valid = await fixture.Store.AuthenticateAndConsumeAsync(hash, ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken);
        Assert.Equal(ApiKeyAuthenticationOutcome.Succeeded, valid.Outcome);
        Assert.DoesNotContain(ApiKeyScopes.TeamRead, valid.Principal!.Scopes);

        var afterApplicationScopeDenial = await fixture.Store.AuthenticateAndConsumeAsync(hash, ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken);
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

        var outcomes = await Task.WhenAll(new[] { disabled, expired, revoked, Hash(99) }.Select(hash => fixture.AuthenticateInNewScopeAsync(hash, ApiKeyStoreFixture.Now)));

        Assert.All(outcomes, result =>
        {
            Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, result.Outcome);
            Assert.Null(result.Principal);
            Assert.Null(result.RetryAfter);
        });
    }

    [Fact]
    public async Task Rotate_and_revoke_serialize_with_presentations()
    {
        await using var fixture = await ApiKeyStoreFixture.CreateAsync(postgres);
        var actor = await fixture.CreateUserAsync("serialize@keys.test");
        var rotateHash = Hash(7);
        var revokeHash = Hash(8);
        var rotateKey = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), rotateHash, "user_abcdefghijk")).Value!;
        var revokeKey = (await fixture.CreateKeyAsync(actor, new(ApiKeyOwnerKind.User, actor, null), revokeHash, "user_bcdefghijkl")).Value!;

        var rotate = fixture.RotateInNewScopeAsync(actor, rotateKey.Id, Hash(9), "user_cdefghijklm");
        var rotateUse = fixture.AuthenticateInNewScopeAsync(rotateHash, ApiKeyStoreFixture.Now);
        await Task.WhenAll(rotate, rotateUse);
        Assert.True((await rotate).Succeeded);
        Assert.Contains((await rotateUse).Outcome, new[] { ApiKeyAuthenticationOutcome.Succeeded, ApiKeyAuthenticationOutcome.Invalid });
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, (await fixture.Store.AuthenticateAndConsumeAsync(rotateHash, ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken)).Outcome);

        var revoke = fixture.RevokeInNewScopeAsync(actor, revokeKey.Id);
        var revokeUse = fixture.AuthenticateInNewScopeAsync(revokeHash, ApiKeyStoreFixture.Now);
        await Task.WhenAll(revoke, revokeUse);
        Assert.True((await revoke).Succeeded);
        Assert.Contains((await revokeUse).Outcome, new[] { ApiKeyAuthenticationOutcome.Succeeded, ApiKeyAuthenticationOutcome.Invalid });
        Assert.Equal(ApiKeyAuthenticationOutcome.Invalid, (await fixture.Store.AuthenticateAndConsumeAsync(revokeHash, ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken)).Outcome);
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
            fixture.Store.AuthenticateAndConsumeAsync(hash, ApiKeyStoreFixture.Now, TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.SerializationFailure,
            Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        Assert.Equal(3, fixture.AuthenticationAttempts);
        Assert.Equal(3, fixture.AuthenticationTransactionCount);
    }

    private static byte[] Hash(int value) => Enumerable.Repeat((byte)value, 32).ToArray();
}

internal sealed class ApiKeyFailureInterceptor : DbCommandInterceptor
{
    private readonly HashSet<Guid> _transactions = [];
    private int _remainingAuthenticationFailures;
    private int _authenticationAttempts;

    internal int AuthenticationAttempts => Volatile.Read(ref _authenticationAttempts);
    internal int AuthenticationTransactionCount
    {
        get { lock (_transactions) return _transactions.Count; }
    }

    internal void FailNextAuthenticationAttempts(int count)
    {
        Volatile.Write(ref _remainingAuthenticationFailures, count);
        Interlocked.Exchange(ref _authenticationAttempts, 0);
        lock (_transactions) _transactions.Clear();
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _remainingAuthenticationFailures) <= 0 ||
            !command.CommandText.Contains("WHERE key_hash", StringComparison.OrdinalIgnoreCase) ||
            !command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(result);
        }

        Interlocked.Increment(ref _authenticationAttempts);
        var transactionId = eventData.Context?.Database.CurrentTransaction?.TransactionId;
        if (transactionId is not null)
        {
            lock (_transactions) _transactions.Add(transactionId.Value);
        }
        Interlocked.Decrement(ref _remainingAuthenticationFailures);
        throw new PostgresException(
            "deterministic serialization failure",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.SerializationFailure);
    }
}
