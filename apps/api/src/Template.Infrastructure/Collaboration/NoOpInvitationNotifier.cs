using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;

namespace Template.Infrastructure.Collaboration;

internal sealed class NoOpInvitationNotifier : IInvitationNotifier
{
    public Task<InvitationNotificationOutcome> NotifyCreatedAsync(
        InvitationNotification notification,
        CancellationToken cancellationToken) =>
        Task.FromResult(InvitationNotificationOutcome.Skipped);
}
