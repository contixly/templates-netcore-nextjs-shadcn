using Template.Application.Collaboration.Ports;
using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Collaboration;

public sealed class InvitationService(
    IInvitationStore invitations,
    IInvitationNotifier notifier,
    TimeProvider timeProvider)
{
    public async Task<InvitationOperationResult<OrganizationInvitationPage>> ListOrganizationAsync(
        UserId actorUserId,
        OrganizationId organizationId,
        InvitationDisplayState? filter,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, "Organization invitation page limit must be between 1 and 100.");
        if (!TryDecode(cursor, out OrganizationInvitationCursorPosition? after))
        {
            return InvitationOperationResult<OrganizationInvitationPage>.Failed(InvitationFailure.InvalidCursor);
        }

        var result = await invitations.ListOrganizationAsync(
            actorUserId,
            organizationId,
            filter,
            after,
            limit,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!result.Succeeded)
        {
            return InvitationOperationResult<OrganizationInvitationPage>.Failed(RequireFailure(result));
        }

        var page = RequireValue(result);
        return InvitationOperationResult<OrganizationInvitationPage>.Success(new(
            page.Items,
            page.Next is null ? null : InvitationCursor.Encode(page.Next)));
    }

    public async Task<InvitationOperationResult<AccountInvitationPage>> ListAccountAsync(
        InvitationActor actor,
        string? cursor,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit, "Account invitation page limit must be between 1 and 100.");
        if (!TryDecode(cursor, out AccountInvitationCursorPosition? after))
        {
            return InvitationOperationResult<AccountInvitationPage>.Failed(InvitationFailure.InvalidCursor);
        }

        var result = await invitations.ListAccountAsync(
            actor,
            after,
            limit,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (!result.Succeeded)
        {
            return InvitationOperationResult<AccountInvitationPage>.Failed(RequireFailure(result));
        }

        var page = RequireValue(result);
        return InvitationOperationResult<AccountInvitationPage>.Success(new(
            page.Items,
            page.Next is null ? null : InvitationCursor.Encode(page.Next)));
    }

    public Task<InvitationOperationResult<InvitationDecision>> GetDecisionAsync(
        InvitationActor actor,
        InvitationId invitationId,
        CancellationToken cancellationToken) =>
        invitations.GetDecisionAsync(actor, invitationId, timeProvider.GetUtcNow(), cancellationToken);

    public async Task<InvitationOperationResult<InvitationView>> CreateAsync(
        CreateInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var result = await invitations.CreateAsync(
            command,
            cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        var invitation = RequireValue(result);
        try
        {
            var outcome = await notifier.NotifyCreatedAsync(
                new InvitationNotification(invitation.Email, $"/invite/{invitation.Id.Value:D}"),
                cancellationToken);
            return outcome == InvitationNotificationOutcome.Failed
                ? result with { Warning = InvitationWarnings.NotificationFailed }
                : result;
        }
        catch (Exception)
        {
            return result with { Warning = InvitationWarnings.NotificationFailed };
        }
    }

    public Task<InvitationOperationResult<AcceptedInvitation>> AcceptAsync(
        AcceptInvitationCommand command,
        CancellationToken cancellationToken) =>
        invitations.AcceptAsync(command, cancellationToken);

    public Task<InvitationOperationResult<InvitationDecision>> RejectAsync(
        RejectInvitationCommand command,
        CancellationToken cancellationToken) =>
        invitations.RejectAsync(command, cancellationToken);

    private static bool TryDecode<TPosition>(string? cursor, out TPosition? after)
        where TPosition : class
    {
        after = default;
        if (cursor is null)
        {
            return true;
        }

        if (typeof(TPosition) == typeof(OrganizationInvitationCursorPosition)
            && InvitationCursor.TryDecode(cursor, out OrganizationInvitationCursorPosition organization))
        {
            after = (TPosition)(object)organization;
            return true;
        }

        if (typeof(TPosition) == typeof(AccountInvitationCursorPosition)
            && InvitationCursor.TryDecode(cursor, out AccountInvitationCursorPosition account))
        {
            after = (TPosition)(object)account;
            return true;
        }

        return false;
    }

    private static InvitationFailure RequireFailure<T>(InvitationOperationResult<T> result)
        where T : class =>
        result.Failure ?? throw new InvalidOperationException("A failed invitation result requires a failure.");

    private static T RequireValue<T>(InvitationOperationResult<T> result)
        where T : class =>
        result.Value ?? throw new InvalidOperationException("A successful invitation result requires a value.");

    private static void ValidateLimit(int limit, string message)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), message);
        }
    }
}
