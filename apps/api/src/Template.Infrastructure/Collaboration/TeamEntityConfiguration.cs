using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Infrastructure.Organizations;

namespace Template.Infrastructure.Collaboration;

public sealed class TeamEntityConfiguration : IEntityTypeConfiguration<TeamEntity>
{
    public void Configure(EntityTypeBuilder<TeamEntity> entity)
    {
        entity.ToTable(
            "teams",
            "organizations",
            table => table.HasCheckConstraint(
                "ck_teams_name",
                """
                char_length(name) BETWEEN 1 AND 50
                AND name = btrim(name)
                AND name ~ '^[[:alnum:] _-]+$'
                """));
        entity.HasKey(value => value.Id).HasName("pk_teams");
        entity.HasAlternateKey(value => new { value.OrganizationId, value.Id })
            .HasName("ak_teams_organization_id_id");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.OrganizationId)
            .HasColumnName("organization_id");
        entity.Property(value => value.Name)
            .HasColumnName("name")
            .HasMaxLength(50);
        entity.Property(value => value.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.CreatedAt,
            value.Id
        })
            .HasDatabaseName("ix_teams_organization_id_created_at_id");
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(value => value.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_teams_organizations_organization_id");
    }
}
