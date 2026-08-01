namespace Template.Infrastructure.Collaboration;

public sealed class TeamMemberEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid TeamId { get; set; }
    public Guid OrganizationMemberId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}
