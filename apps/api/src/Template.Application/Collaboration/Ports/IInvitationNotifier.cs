namespace Template.Application.Collaboration.Ports;

public interface IInvitationNotifier
{
    Task<InvitationNotificationOutcome> NotifyCreatedAsync(
        InvitationNotification notification,
        CancellationToken cancellationToken);
}
