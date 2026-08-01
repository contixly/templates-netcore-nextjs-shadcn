using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Infrastructure.Identity;

namespace Template.Infrastructure.Organizations;

public sealed class OrganizationMemberEntityConfiguration
    : IEntityTypeConfiguration<OrganizationMemberEntity>
{
    public void Configure(EntityTypeBuilder<OrganizationMemberEntity> entity)
    {
        entity.ToTable(
            "members",
            "organizations",
            table => table.HasCheckConstraint(
                "ck_members_role",
                "role IN ('owner', 'admin', 'member')"));
        entity.HasKey(value => value.Id).HasName("pk_members");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.OrganizationId)
            .HasColumnName("organization_id");
        entity.Property(value => value.UserId).HasColumnName("user_id");
        entity.Property(value => value.Role)
            .HasColumnName("role")
            .HasMaxLength(6);
        entity.Property(value => value.JoinedAt)
            .HasColumnName("joined_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        entity.HasIndex(value => new { value.OrganizationId, value.UserId })
            .IsUnique()
            .HasDatabaseName("ux_members_organization_id_user_id");
        entity.HasIndex(value => new { value.UserId, value.OrganizationId })
            .HasDatabaseName("ix_members_user_id_organization_id");
        entity.HasIndex(value => new
        {
            value.UserId,
            value.JoinedAt,
            value.Id
        })
            .HasDatabaseName("ix_members_user_id_joined_at_id");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.JoinedAt,
            value.Id
        })
            .HasDatabaseName("ix_members_organization_id_joined_at_id");
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(value => value.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_members_organizations_organization_id");
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_members_users_user_id");
    }
}
