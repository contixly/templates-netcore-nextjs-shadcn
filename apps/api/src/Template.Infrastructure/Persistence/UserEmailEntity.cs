namespace Template.Infrastructure.Persistence;

public sealed class UserEmailEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
