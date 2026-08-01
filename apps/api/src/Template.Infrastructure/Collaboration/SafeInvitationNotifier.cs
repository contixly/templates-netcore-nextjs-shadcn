using Microsoft.Extensions.Logging;
using Template.Application.Collaboration;
using Template.Application.Collaboration.Ports;

namespace Template.Infrastructure.Collaboration;

internal sealed class SafeInvitationNotifier(
    IInvitationNotifier inner,
    ILogger<SafeInvitationNotifier> logger)
    : IInvitationNotifier
{
    public async Task<InvitationNotificationOutcome> NotifyCreatedAsync(
        InvitationNotification notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await inner.NotifyCreatedAsync(
                notification,
                cancellationToken);
            LogOutcome(outcome);
            return outcome;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            LogOutcome(InvitationNotificationOutcome.Failed);
            return InvitationNotificationOutcome.Failed;
        }
    }

    private void LogOutcome(InvitationNotificationOutcome outcome)
    {
        if (outcome == InvitationNotificationOutcome.Failed)
        {
            logger.LogWarning(
                "Invitation notification completed with outcome {Outcome}.",
                outcome.ToString());
            return;
        }

        logger.LogInformation(
            "Invitation notification completed with outcome {Outcome}.",
            outcome.ToString());
    }
}
