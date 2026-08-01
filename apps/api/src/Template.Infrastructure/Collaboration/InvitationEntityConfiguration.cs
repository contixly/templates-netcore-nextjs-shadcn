using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;

namespace Template.Infrastructure.Collaboration;

public sealed class InvitationEntityConfiguration
    : IEntityTypeConfiguration<InvitationEntity>
{
    public void Configure(EntityTypeBuilder<InvitationEntity> entity)
    {
        entity.ToTable(
            "invitations",
            "organizations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_invitations_email",
                    "char_length(email) BETWEEN 1 AND 254 AND email = lower(email)");
                table.HasCheckConstraint(
                    "ck_invitations_role",
                    "role IN ('owner', 'admin', 'member')");
                table.HasCheckConstraint(
                    "ck_invitations_status",
                    "status IN ('pending', 'accepted', 'rejected', 'canceled')");
                table.HasCheckConstraint(
                    "ck_invitations_expiry",
                    "expires_at > created_at");
            });
        entity.HasKey(value => value.Id).HasName("pk_invitations");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.OrganizationId)
            .HasColumnName("organization_id");
        entity.Property(value => value.TeamId).HasColumnName("team_id");
        entity.Property(value => value.Email)
            .HasColumnName("email")
            .HasMaxLength(254);
        entity.Property(value => value.Role)
            .HasColumnName("role")
            .HasMaxLength(6);
        entity.Property(value => value.Status)
            .HasColumnName("status")
            .HasMaxLength(8);
        entity.Property(value => value.InviterUserId)
            .HasColumnName("inviter_user_id");
        entity.Property(value => value.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        entity.HasIndex(value => new { value.OrganizationId, value.Email })
            .IsUnique()
            .HasFilter("status = 'pending'")
            .HasDatabaseName(
                "ux_invitations_organization_id_email_pending");
        entity.HasIndex(value => value.InviterUserId)
            .HasDatabaseName("ix_invitations_inviter_user_id");
        entity.HasIndex(value => new { value.OrganizationId, value.TeamId })
            .HasDatabaseName("ix_invitations_organization_id_team_id");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.CreatedAt,
            value.Id
        })
            .IsDescending(false, true, true)
            .HasDatabaseName(
                "ix_invitations_organization_id_created_at_id");
        entity.HasIndex(value => new
        {
            value.Email,
            value.Status,
            value.ExpiresAt,
            value.CreatedAt,
            value.Id
        })
            .IsDescending(false, false, false, true, true)
            .HasDatabaseName(
                "ix_invitations_email_status_expires_at_created_at_id");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.InviterUserId,
            value.Status,
            value.ExpiresAt
        })
            .HasDatabaseName(
                "ix_invitations_organization_inviter_status_expires_at");
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(value => value.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(
                "fk_invitations_organizations_organization_id");
        entity.HasOne<TeamEntity>()
            .WithMany()
            .HasForeignKey(value => new { value.OrganizationId, value.TeamId })
            .HasPrincipalKey(team => new { team.OrganizationId, team.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_invitations_teams_organization_id_team_id");
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.InviterUserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_invitations_users_inviter_user_id");
    }
}
