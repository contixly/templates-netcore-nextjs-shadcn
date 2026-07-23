namespace Template.Api.Features.Health;

internal sealed record HealthResponse(string Status, DateTimeOffset Timestamp);
