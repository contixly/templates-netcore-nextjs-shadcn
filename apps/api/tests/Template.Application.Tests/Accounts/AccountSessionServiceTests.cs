using Template.Application.Accounts;
using Template.Application.Accounts.Ports;
using Template.Domain.Authentication;

namespace Template.Application.Tests.Accounts;

public sealed class AccountSessionServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly UserId UserId = new(Guid.Parse("01987712-9e00-7000-8000-000000000001"));
    private static readonly SessionId CurrentSessionId =
        new(Guid.Parse("01987712-9e00-7000-8000-000000000002"));
    private static readonly SessionId OtherSessionId =
        new(Guid.Parse("01987712-9e00-7000-8000-000000000003"));
    private static readonly DateTimeOffset LastSeenAt =
        new DateTimeOffset(2026, 7, 28, 12, 34, 56, TimeSpan.Zero).AddTicks(7890);

    [Fact]
    public void CursorRoundTripsWithoutExposingItsComponents()
    {
        var cursor = new SessionCursor(LastSeenAt, OtherSessionId);

        var encoded = SessionCursor.Encode(cursor);

        Assert.DoesNotContain(LastSeenAt.ToString("O"), encoded);
        Assert.DoesNotContain(OtherSessionId.Value.ToString(), encoded);
        Assert.True(SessionCursor.TryDecode(encoded, out var decoded));
        Assert.Equal(cursor, decoded);
    }

    [Fact]
    public void CursorRejectsMalformedValuesAndTampering()
    {
        var encoded = SessionCursor.Encode(
            new SessionCursor(LastSeenAt, OtherSessionId));
        var replacement = encoded[^1] == 'A' ? 'B' : 'A';
        var tampered = encoded[..^1] + replacement;

        Assert.False(SessionCursor.TryDecode("", out _));
        Assert.False(SessionCursor.TryDecode("not+base64url", out _));
        Assert.False(SessionCursor.TryDecode($"{encoded}=", out _));
        Assert.False(SessionCursor.TryDecode(tampered, out _));
    }

    [Fact]
    public async Task ValidCursorIsDecodedBeforeStoreQuery()
    {
        var store = new FakeAccountSessionStore();
        var service = new AccountSessionService(store);
        var expected = new SessionCursor(LastSeenAt, OtherSessionId);

        var result = await service.ListAsync(
            UserId,
            SessionCursor.Encode(expected),
            limit: 20,
            Ct);

        Assert.Null(result.Failure);
        Assert.Equal(expected, Assert.Single(store.Cursors));
        Assert.Equal(20, Assert.Single(store.Limits));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    public async Task InvalidCursorIsRejectedBeforeStoreQuery(string cursor)
    {
        var store = new FakeAccountSessionStore();
        var service = new AccountSessionService(store);

        var result = await service.ListAsync(UserId, cursor, limit: 20, Ct);

        Assert.Null(result.Value);
        Assert.Equal(AccountFailure.InvalidCursor, result.Failure);
        Assert.Empty(store.Cursors);
    }

    [Fact]
    public async Task MissingCursorStartsTheFirstPage()
    {
        var store = new FakeAccountSessionStore();
        var service = new AccountSessionService(store);

        var result = await service.ListAsync(UserId, cursor: null, limit: 20, Ct);

        Assert.Null(result.Failure);
        Assert.Null(Assert.Single(store.Cursors));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task PageLimitOutsideOneToOneHundredIsRejected(int limit)
    {
        var store = new FakeAccountSessionStore();
        var service = new AccountSessionService(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ListAsync(UserId, cursor: null, limit, Ct));

        Assert.Empty(store.Cursors);
    }

    [Fact]
    public async Task CurrentSessionCannotBeRevoked()
    {
        var store = new FakeAccountSessionStore();
        var service = new AccountSessionService(store);

        var result = await service.RevokeAsync(
            UserId,
            CurrentSessionId,
            CurrentSessionId,
            Ct);

        Assert.Equal(AccountFailure.CurrentSessionCannotBeRevoked, result.Failure);
        Assert.Empty(store.RevokedSessionIds);
    }

    [Fact]
    public async Task ForeignAndMissingSessionsHaveEquivalentFailure()
    {
        var store = new FakeAccountSessionStore();
        var service = new AccountSessionService(store);
        var foreignUserId =
            new UserId(Guid.Parse("01987712-9e00-7000-8000-000000000004"));
        var sharedSessionId =
            new SessionId(Guid.Parse("01987712-9e00-7000-8000-000000000005"));
        var missingSessionId =
            new SessionId(Guid.Parse("01987712-9e00-7000-8000-000000000006"));
        store.OwnedSessions.Add((foreignUserId, sharedSessionId));

        var foreign = await service.RevokeAsync(
            UserId,
            sharedSessionId,
            CurrentSessionId,
            Ct);
        var missing = await service.RevokeAsync(
            UserId,
            missingSessionId,
            CurrentSessionId,
            Ct);

        Assert.Null(foreign.Value);
        Assert.Null(missing.Value);
        Assert.Equal(AccountFailure.SessionNotFound, foreign.Failure);
        Assert.Equal(foreign.Failure, missing.Failure);
        Assert.Equal(
            [(UserId, sharedSessionId), (UserId, missingSessionId)],
            store.RevokeRequests);
        Assert.Contains((foreignUserId, sharedSessionId), store.OwnedSessions);
    }

    [Fact]
    public async Task OwnedNonCurrentSessionIsRevoked()
    {
        var store = new FakeAccountSessionStore();
        store.OwnedSessions.Add((UserId, OtherSessionId));
        var service = new AccountSessionService(store);

        var result = await service.RevokeAsync(
            UserId,
            OtherSessionId,
            CurrentSessionId,
            Ct);

        Assert.Null(result.Failure);
        Assert.Equal(OtherSessionId, result.Value!.SessionId);
        Assert.Equal(OtherSessionId, Assert.Single(store.RevokedSessionIds));
    }

    [Fact]
    public async Task RevokeOthersPreservesTheCurrentSession()
    {
        var store = new FakeAccountSessionStore();
        store.OwnedSessions.UnionWith(
            [(UserId, CurrentSessionId), (UserId, OtherSessionId)]);
        var service = new AccountSessionService(store);

        var revoked = await service.RevokeOthersAsync(
            UserId,
            CurrentSessionId,
            Ct);

        Assert.Equal(1, revoked);
        Assert.Contains((UserId, CurrentSessionId), store.OwnedSessions);
        Assert.DoesNotContain((UserId, OtherSessionId), store.OwnedSessions);
        Assert.Equal(CurrentSessionId, Assert.Single(store.PreservedSessionIds));
    }

    private sealed class FakeAccountSessionStore : IAccountSessionStore
    {
        public HashSet<(UserId UserId, SessionId SessionId)> OwnedSessions { get; } =
            [];
        public List<SessionCursor?> Cursors { get; } = [];
        public List<int> Limits { get; } = [];
        public List<(UserId UserId, SessionId SessionId)> RevokeRequests { get; } = [];
        public List<SessionId> RevokedSessionIds { get; } = [];
        public List<SessionId> PreservedSessionIds { get; } = [];

        public Task<CursorPage<AccountSession>> ListAsync(
            UserId userId,
            SessionCursor? cursor,
            int limit,
            CancellationToken ct)
        {
            Cursors.Add(cursor);
            Limits.Add(limit);
            return Task.FromResult(new CursorPage<AccountSession>([], null));
        }

        public Task<bool> RevokeAsync(
            UserId userId,
            SessionId sessionId,
            CancellationToken ct)
        {
            RevokeRequests.Add((userId, sessionId));
            if (!OwnedSessions.Remove((userId, sessionId)))
            {
                return Task.FromResult(false);
            }

            RevokedSessionIds.Add(sessionId);
            return Task.FromResult(true);
        }

        public Task<int> RevokeOthersAsync(
            UserId userId,
            SessionId current,
            CancellationToken ct)
        {
            PreservedSessionIds.Add(current);
            var revoked = OwnedSessions.RemoveWhere(
                session => session.UserId == userId && session.SessionId != current);
            return Task.FromResult(revoked);
        }
    }
}
