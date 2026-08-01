namespace Template.Infrastructure.Collaboration;

public sealed class InvitationEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? TeamId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public Guid InviterUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
