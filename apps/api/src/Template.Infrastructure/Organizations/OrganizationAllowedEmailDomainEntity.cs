namespace Template.Infrastructure.Organizations;

public sealed class OrganizationAllowedEmailDomainEntity
{
    public Guid OrganizationId { get; set; }
    public required string Domain { get; set; }
}
