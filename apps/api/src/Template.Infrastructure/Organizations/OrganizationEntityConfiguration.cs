using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Template.Infrastructure.Organizations;

public sealed class OrganizationEntityConfiguration
    : IEntityTypeConfiguration<OrganizationEntity>
{
    public void Configure(EntityTypeBuilder<OrganizationEntity> entity)
    {
        entity.ToTable(
            "organizations",
            "organizations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_organizations_name",
                    "char_length(name) BETWEEN 1 AND 50");
                table.HasCheckConstraint(
                    "ck_organizations_slug",
                    """
                    char_length(slug) BETWEEN 1 AND 64
                    AND slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'
                    """);
            });
        entity.HasKey(value => value.Id).HasName("pk_organizations");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.Name)
            .HasColumnName("name")
            .HasMaxLength(50);
        entity.Property(value => value.Slug)
            .HasColumnName("slug")
            .HasMaxLength(64);
        entity.Property(value => value.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        entity.HasIndex(value => value.Slug)
            .IsUnique()
            .HasDatabaseName("ux_organizations_slug");
    }
}
