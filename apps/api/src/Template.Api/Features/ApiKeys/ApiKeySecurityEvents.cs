namespace Template.Api.Features.ApiKeys;

internal static partial class ApiKeySecurityEvents
{
    [LoggerMessage(
        EventId = 3160,
        Level = LogLevel.Information,
        Message =
            "API key operation {ApiKeyOperation} finished with {ApiKeyOutcome}; UserId={UserId}; OwnerKind={OwnerKind}; OwnerId={OwnerId}; ApiKeyId={ApiKeyId}")]
    internal static partial void Write(
        ILogger logger,
        string apiKeyOperation,
        string apiKeyOutcome,
        Guid userId,
        string ownerKind,
        Guid ownerId,
        Guid? apiKeyId);
}
