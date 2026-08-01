using Template.Domain.Authentication;
using Template.Domain.Collaboration;
using Template.Domain.Organizations;

namespace Template.Application.Collaboration.Ports;

public interface IInvitationStore
{
    Task<InvitationOperationResult<
        InvitationStorePage<InvitationView, OrganizationInvitationCursorPosition>>>
        ListOrganizationAsync(
            UserId actorUserId,
            OrganizationId organizationId,
            InvitationDisplayState? filter,
            OrganizationInvitationCursorPosition? after,
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken);

    Task<InvitationOperationResult<
        InvitationStorePage<InvitationView, AccountInvitationCursorPosition>>>
        ListAccountAsync(
            InvitationActor actor,
            AccountInvitationCursorPosition? after,
            int limit,
            DateTimeOffset now,
            CancellationToken cancellationToken);

    Task<InvitationOperationResult<InvitationDecision>> GetDecisionAsync(
        InvitationActor actor,
        InvitationId invitationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<InvitationOperationResult<InvitationView>> CreateAsync(
        CreateInvitationCommand command,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<InvitationOperationResult<AcceptedInvitation>> AcceptAsync(
        AcceptInvitationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<InvitationOperationResult<InvitationDecision>> RejectAsync(
        RejectInvitationCommand command,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
