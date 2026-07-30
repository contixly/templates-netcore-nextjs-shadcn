using Microsoft.Extensions.Logging;

namespace Template.Api.Features.Account;

internal static partial class AccountSecurityEvents
{
    [LoggerMessage(
        EventId = 3120,
        Level = LogLevel.Information,
        Message =
            "Account operation {AccountOperation} finished with {AccountOutcome}; UserId={UserId}; SessionId={SessionId}; ProviderId={ProviderId}")]
    internal static partial void Write(
        ILogger logger,
        string accountOperation,
        string accountOutcome,
        Guid userId,
        Guid? sessionId,
        string? providerId);
}
