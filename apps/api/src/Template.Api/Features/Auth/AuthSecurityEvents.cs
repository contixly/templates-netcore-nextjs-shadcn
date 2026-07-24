using Microsoft.Extensions.Logging;

namespace Template.Api.Features.Auth;

internal static partial class AuthSecurityEvents
{
    [LoggerMessage(
        EventId = 3100,
        Level = LogLevel.Information,
        Message =
            "Authentication operation {AuthOperation} finished with {AuthOutcome}; UserId={UserId}; SessionId={SessionId}")]
    internal static partial void Write(
        ILogger logger,
        string authOperation,
        string authOutcome,
        Guid? userId,
        Guid? sessionId);
}
