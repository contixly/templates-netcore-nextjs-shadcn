using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Tests.Collaboration;

public sealed class InvitationServiceTests
{
    private static readonly UserId Actor = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly UserId Recipient = new(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    private static readonly SessionId Session = new(Guid.Parse("00000000-0000-0000-0000-000000000003"));
    private static readonly OrganizationId Organization = new(Guid.Parse("00000000-0000-0000-0000-000000000010"));
    private static readonly InvitationId Invitation = new(Guid.Parse("00000000-0000-0000-0000-000000000020"));
    private static readonly InvitationActor RecipientActor = new(Recipient, "recipient@example.com", true);

    [Fact]
    public async Task Successful_create_notifies_after_the_store_returns_success()
    {
        var calls = new List<string>();
        var store = new RecordingInvitationStore(calls)
        {
            CreateResult = InvitationOperationResult<InvitationView>.Success(InvitationTestData.View())
        };
        var notifier = new RecordingInvitationNotifier(calls);
        var service = new InvitationService(store, notifier, TimeProvider.System);
        var command = CreateCommand();

        var result = await service.CreateAsync(command, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(["store", "notify"], calls);
        Assert.Equal($"/invite/{Invitation.Value:D}", notifier.Last!.InvitationPath);
        Assert.Equal(command.Email, notifier.Last.RecipientEmail);
    }

    [Fact]
    public async Task Notification_failure_does_not_replace_committed_success()
    {
        var invitation = InvitationTestData.View();
        var store = new RecordingInvitationStore
        {
            CreateResult = InvitationOperationResult<InvitationView>.Success(invitation)
        };
        var notifier = new RecordingInvitationNotifier
        {
            Result = InvitationNotificationOutcome.Failed
        };
        var service = new InvitationService(store, notifier, TimeProvider.System);

        var result = await service.CreateAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(invitation, result.Value);
        Assert.Equal(1, notifier.Calls);
        Assert.Equal("notification_failed", result.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notification_exception_after_store_success_returns_a_safe_warning(bool cancellationStyle)
    {
        var invitation = InvitationTestData.View();
        var store = new RecordingInvitationStore
        {
            CreateResult = InvitationOperationResult<InvitationView>.Success(invitation)
        };
        var notifier = new RecordingInvitationNotifier
        {
            CancellationFailure = cancellationStyle,
            ExceptionToThrow = cancellationStyle
                ? null
                : new InvalidOperationException("provider detail must not escape")
        };
        var service = new InvitationService(store, notifier, TimeProvider.System);

        var result = await service.CreateAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Same(invitation, result.Value);
        Assert.Equal("notification_failed", result.Warning);
        Assert.Equal(1, notifier.Calls);
    }

    [Fact]
    public async Task Store_failure_does_not_invoke_the_notifier()
    {
        var store = new RecordingInvitationStore
        {
            CreateResult = InvitationOperationResult<InvitationView>.Failed(InvitationFailure.AlreadyExists)
        };
        var notifier = new RecordingInvitationNotifier();
        var service = new InvitationService(store, notifier, TimeProvider.System);

        var result = await service.CreateAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.AlreadyExists, result.Failure);
        Assert.Equal(0, notifier.Calls);
    }

    [Fact]
    public async Task Create_supplies_an_exact_48_hour_expiry_from_the_time_provider()
    {
        var now = DateTimeOffset.Parse("2026-08-01T12:34:56Z");
        var store = new RecordingInvitationStore();
        var service = new InvitationService(store, new RecordingInvitationNotifier(), new FixedTimeProvider(now));

        await service.CreateAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(now.AddHours(48), store.LastCreateExpiresAt);
    }

    [Fact]
    public async Task Organization_list_decodes_its_cursor_and_encodes_only_the_store_continuation()
    {
        var after = new OrganizationInvitationCursorPosition(DateTimeOffset.Parse("2026-08-01T00:00:00Z"), Invitation);
        var next = new OrganizationInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000021")));
        var store = new RecordingInvitationStore
        {
            OrganizationListResult = InvitationOperationResult<InvitationStorePage<InvitationView, OrganizationInvitationCursorPosition>>.Success(new([], next))
        };
        var service = new InvitationService(store, new RecordingInvitationNotifier(), TimeProvider.System);

        var result = await service.ListOrganizationAsync(
            Actor,
            Organization,
            InvitationDisplayState.Pending,
            InvitationCursor.Encode(after),
            25,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(after, store.LastOrganizationAfter);
        Assert.Equal(InvitationDisplayState.Pending, store.LastOrganizationFilter);
        Assert.True(InvitationCursor.TryDecode(result.Value!.NextCursor, out OrganizationInvitationCursorPosition decoded));
        Assert.Equal(next, decoded);
    }

    [Fact]
    public async Task Account_list_rejects_an_organization_cursor_without_store_access()
    {
        var store = new RecordingInvitationStore();
        var service = new InvitationService(store, new RecordingInvitationNotifier(), TimeProvider.System);
        var cursor = InvitationCursor.Encode(new OrganizationInvitationCursorPosition(
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"), Invitation));

        var result = await service.ListAccountAsync(RecipientActor, cursor, 50, TestContext.Current.CancellationToken);

        Assert.Equal(InvitationFailure.InvalidCursor, result.Failure);
        Assert.Equal(0, store.AccountListCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Lists_reject_out_of_range_limits(int limit)
    {
        var store = new RecordingInvitationStore();
        var service = new InvitationService(store, new RecordingInvitationNotifier(), TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListOrganizationAsync(Actor, Organization, null, null, limit, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListAccountAsync(RecipientActor, null, limit, TestContext.Current.CancellationToken));

        Assert.Equal(0, store.OrganizationListCalls);
        Assert.Equal(0, store.AccountListCalls);
    }

    [Fact]
    public async Task Decision_accept_and_reject_pass_current_time_and_typed_actor_session_and_invitation_ids()
    {
        var now = DateTimeOffset.Parse("2026-08-01T12:34:56Z");
        var store = new RecordingInvitationStore();
        var service = new InvitationService(store, new RecordingInvitationNotifier(), new FixedTimeProvider(now));

        await service.ListAccountAsync(RecipientActor, null, 50, TestContext.Current.CancellationToken);
        await service.GetDecisionAsync(RecipientActor, Invitation, TestContext.Current.CancellationToken);
        await service.AcceptAsync(new AcceptInvitationCommand(RecipientActor, Session, Invitation), TestContext.Current.CancellationToken);
        await service.RejectAsync(new RejectInvitationCommand(RecipientActor, Invitation), TestContext.Current.CancellationToken);

        Assert.Equal(now, store.LastAccountNow);
        Assert.Equal(now, store.LastDecisionNow);
        Assert.Equal(now, store.LastAcceptNow);
        Assert.Equal(now, store.LastRejectNow);
        Assert.Equal(RecipientActor, store.LastAccountActor);
        Assert.Equal(RecipientActor, store.LastDecisionActor);
        Assert.Equal(new AcceptInvitationCommand(RecipientActor, Session, Invitation), store.LastAccept);
        Assert.Equal(new RejectInvitationCommand(RecipientActor, Invitation), store.LastReject);
    }

    [Theory]
    [InlineData(InvitationFailure.InvalidCursor)]
    [InlineData(InvitationFailure.NotFound)]
    [InlineData(InvitationFailure.PermissionDenied)]
    [InlineData(InvitationFailure.AlreadyExists)]
    [InlineData(InvitationFailure.RecipientAlreadyMember)]
    [InlineData(InvitationFailure.TeamInvalid)]
    [InlineData(InvitationFailure.DomainRestricted)]
    [InlineData(InvitationFailure.RecipientMismatch)]
    [InlineData(InvitationFailure.EmailVerificationRequired)]
    [InlineData(InvitationFailure.Expired)]
    [InlineData(InvitationFailure.NotPending)]
    [InlineData(InvitationFailure.MembershipConflict)]
    [InlineData(InvitationFailure.LimitReached)]
    [InlineData(InvitationFailure.ConcurrencyConflict)]
    public async Task Create_propagates_every_store_failure(InvitationFailure failure)
    {
        var store = new RecordingInvitationStore
        {
            CreateResult = InvitationOperationResult<InvitationView>.Failed(failure)
        };
        var service = new InvitationService(store, new RecordingInvitationNotifier(), TimeProvider.System);

        var result = await service.CreateAsync(CreateCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(failure, result.Failure);
    }

    private static CreateInvitationCommand CreateCommand() =>
        new(Actor, Organization, "recipient@example.com", OrganizationRole.Member, null);
}

internal static class InvitationTestData
{
    internal static InvitationView View() => new(
        new InvitationId(Guid.Parse("00000000-0000-0000-0000-000000000020")),
        new OrganizationId(Guid.Parse("00000000-0000-0000-0000-000000000010")),
        "Example",
        "example",
        null,
        null,
        "recipient@example.com",
        OrganizationRole.Member,
        InvitationStatus.Pending,
        InvitationDisplayState.Pending,
        DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
        DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        new UserId(Guid.Parse("00000000-0000-0000-0000-000000000001")),
        "Inviter");
}

internal sealed class RecordingInvitationStore : IInvitationStore
{
    private readonly List<string>? calls;

    internal RecordingInvitationStore(List<string>? calls = null) => this.calls = calls;

    internal InvitationOperationResult<InvitationStorePage<InvitationView, OrganizationInvitationCursorPosition>> OrganizationListResult { get; set; } =
        InvitationOperationResult<InvitationStorePage<InvitationView, OrganizationInvitationCursorPosition>>.Success(new([], null));
    internal InvitationOperationResult<InvitationStorePage<InvitationView, AccountInvitationCursorPosition>> AccountListResult { get; set; } =
        InvitationOperationResult<InvitationStorePage<InvitationView, AccountInvitationCursorPosition>>.Success(new([], null));
    internal InvitationOperationResult<InvitationView> CreateResult { get; set; } =
        InvitationOperationResult<InvitationView>.Failed(InvitationFailure.NotFound);
    internal int OrganizationListCalls { get; private set; }
    internal int AccountListCalls { get; private set; }
    internal OrganizationInvitationCursorPosition? LastOrganizationAfter { get; private set; }
    internal InvitationDisplayState? LastOrganizationFilter { get; private set; }
    internal DateTimeOffset? LastCreateExpiresAt { get; private set; }
    internal InvitationActor? LastAccountActor { get; private set; }
    internal DateTimeOffset? LastAccountNow { get; private set; }
    internal InvitationActor? LastDecisionActor { get; private set; }
    internal DateTimeOffset? LastDecisionNow { get; private set; }
    internal AcceptInvitationCommand? LastAccept { get; private set; }
    internal DateTimeOffset? LastAcceptNow { get; private set; }
    internal RejectInvitationCommand? LastReject { get; private set; }
    internal DateTimeOffset? LastRejectNow { get; private set; }

    public Task<InvitationOperationResult<InvitationStorePage<InvitationView, OrganizationInvitationCursorPosition>>> ListOrganizationAsync(UserId actorUserId, OrganizationId organizationId, InvitationDisplayState? filter, OrganizationInvitationCursorPosition? after, int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        OrganizationListCalls++;
        LastOrganizationAfter = after;
        LastOrganizationFilter = filter;
        return Task.FromResult(OrganizationListResult);
    }

    public Task<InvitationOperationResult<InvitationStorePage<InvitationView, AccountInvitationCursorPosition>>> ListAccountAsync(InvitationActor actor, AccountInvitationCursorPosition? after, int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        AccountListCalls++;
        LastAccountActor = actor;
        LastAccountNow = now;
        return Task.FromResult(AccountListResult);
    }

    public Task<InvitationOperationResult<InvitationDecision>> GetDecisionAsync(InvitationActor actor, InvitationId invitationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LastDecisionActor = actor;
        LastDecisionNow = now;
        return Task.FromResult(InvitationOperationResult<InvitationDecision>.Failed(InvitationFailure.NotFound));
    }

    public Task<InvitationOperationResult<InvitationView>> CreateAsync(CreateInvitationCommand command, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        calls?.Add("store");
        LastCreateExpiresAt = expiresAt;
        return Task.FromResult(CreateResult);
    }

    public Task<InvitationOperationResult<AcceptedInvitation>> AcceptAsync(AcceptInvitationCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LastAccept = command;
        LastAcceptNow = now;
        return Task.FromResult(InvitationOperationResult<AcceptedInvitation>.Failed(InvitationFailure.NotFound));
    }

    public Task<InvitationOperationResult<InvitationDecision>> RejectAsync(RejectInvitationCommand command, DateTimeOffset now, CancellationToken cancellationToken)
    {
        LastReject = command;
        LastRejectNow = now;
        return Task.FromResult(InvitationOperationResult<InvitationDecision>.Failed(InvitationFailure.NotFound));
    }
}

internal sealed class RecordingInvitationNotifier : IInvitationNotifier
{
    private readonly List<string>? calls;

    internal RecordingInvitationNotifier(List<string>? calls = null) => this.calls = calls;

    internal InvitationNotificationOutcome Result { get; set; } = InvitationNotificationOutcome.Completed;
    internal InvitationNotification? Last { get; private set; }
    internal int Calls { get; private set; }
    internal Exception? ExceptionToThrow { get; set; }
    internal bool CancellationFailure { get; set; }

    public Task<InvitationNotificationOutcome> NotifyCreatedAsync(InvitationNotification notification, CancellationToken cancellationToken)
    {
        calls?.Add("notify");
        Calls++;
        Last = notification;
        if (CancellationFailure)
        {
            return Task.FromCanceled<InvitationNotificationOutcome>(new CancellationToken(canceled: true));
        }

        return ExceptionToThrow is null
            ? Task.FromResult(Result)
            : Task.FromException<InvitationNotificationOutcome>(ExceptionToThrow);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
