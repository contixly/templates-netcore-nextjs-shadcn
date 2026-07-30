using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Template.Infrastructure.Organizations;

public sealed class OrganizationAllowedEmailDomainEntityConfiguration
    : IEntityTypeConfiguration<OrganizationAllowedEmailDomainEntity>
{
    public void Configure(
        EntityTypeBuilder<OrganizationAllowedEmailDomainEntity> entity)
    {
        entity.ToTable(
            "allowed_email_domains",
            "organizations",
            table => table.HasCheckConstraint(
                "ck_allowed_email_domains_domain",
                "char_length(domain) BETWEEN 1 AND 253 AND domain = lower(domain)"));
        entity.HasKey(value => new { value.OrganizationId, value.Domain })
            .HasName("pk_allowed_email_domains");
        entity.Property(value => value.OrganizationId)
            .HasColumnName("organization_id");
        entity.Property(value => value.Domain)
            .HasColumnName("domain")
            .HasMaxLength(253);
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(value => value.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(
                "fk_allowed_email_domains_organizations_organization_id");
    }
}
