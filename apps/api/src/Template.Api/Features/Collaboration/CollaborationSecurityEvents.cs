using Microsoft.Extensions.Logging;

namespace Template.Api.Features.Collaboration;

internal static partial class CollaborationSecurityEvents
{
    [LoggerMessage(
        EventId = 3160,
        Level = LogLevel.Information,
        Message =
            "Collaboration operation {CollaborationOperation} finished with {CollaborationOutcome}; UserId={UserId}; SessionId={SessionId}; OrganizationId={OrganizationId}; TeamId={TeamId}; TargetUserId={TargetUserId}; ResultCount={ResultCount}")]
    internal static partial void Write(
        ILogger logger,
        string collaborationOperation,
        string collaborationOutcome,
        Guid userId,
        Guid sessionId,
        Guid? organizationId,
        Guid? teamId,
        Guid? targetUserId,
        int? resultCount);

    [LoggerMessage(
        EventId = 3161,
        Level = LogLevel.Information,
        Message =
            "Collaboration operation {CollaborationOperation} finished with {CollaborationOutcome}; UserId={UserId}; SessionId={SessionId}; OrganizationId={OrganizationId}; TeamId={TeamId}; InvitationId={InvitationId}; ResultCount={ResultCount}")]
    internal static partial void WriteInvitation(
        ILogger logger,
        string collaborationOperation,
        string collaborationOutcome,
        Guid userId,
        Guid sessionId,
        Guid? organizationId,
        Guid? teamId,
        Guid? invitationId,
        int? resultCount);
}
