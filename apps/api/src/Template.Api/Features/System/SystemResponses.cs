namespace Template.Api.Features.System;

internal sealed record SystemStatusResponse(
    string Status,
    string ApiVersion,
    DateTimeOffset Timestamp,
    string? Echo);

internal sealed record AuthenticatedResponse(string Status);
