using Microsoft.Extensions.Logging;

namespace Template.Api.Features.Organizations;

internal static partial class OrganizationSecurityEvents
{
    [LoggerMessage(
        EventId = 3140,
        Level = LogLevel.Information,
        Message =
            "Organization operation {OrganizationOperation} finished with {OrganizationOutcome}; UserId={UserId}; SessionId={SessionId}; OrganizationId={OrganizationId}; MemberId={MemberId}")]
    internal static partial void Write(
        ILogger logger,
        string organizationOperation,
        string organizationOutcome,
        Guid userId,
        Guid sessionId,
        Guid? organizationId,
        Guid? memberId);
}
