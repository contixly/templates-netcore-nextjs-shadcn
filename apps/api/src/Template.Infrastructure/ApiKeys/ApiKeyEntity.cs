namespace Template.Infrastructure.ApiKeys;

public sealed class ApiKeyEntity
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string Name { get; set; }
    public required byte[] KeyHash { get; set; }
    public required string KeyStart { get; set; }
    public required string[] Scopes { get; set; }
    public bool Enabled { get; set; }
    public bool RateLimitEnabled { get; set; }
    public int RateLimitWindowSeconds { get; set; }
    public int RateLimitMax { get; set; }
    public DateTimeOffset? WindowStartedAt { get; set; }
    public int RequestCount { get; set; }
    public DateTimeOffset? LastRequestAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
