using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Infrastructure.Organizations;

namespace Template.Infrastructure.Collaboration;

public sealed class TeamMemberEntityConfiguration
    : IEntityTypeConfiguration<TeamMemberEntity>
{
    public void Configure(EntityTypeBuilder<TeamMemberEntity> entity)
    {
        entity.ToTable("team_members", "organizations");
        entity.HasKey(value => value.Id).HasName("pk_team_members");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.OrganizationId)
            .HasColumnName("organization_id");
        entity.Property(value => value.TeamId).HasColumnName("team_id");
        entity.Property(value => value.OrganizationMemberId)
            .HasColumnName("organization_member_id");
        entity.Property(value => value.JoinedAt)
            .HasColumnName("joined_at")
            .HasColumnType("timestamp with time zone");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.OrganizationMemberId
        })
            .HasDatabaseName(
                "ix_team_members_organization_id_organization_member_id");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.TeamId
        })
            .HasDatabaseName("ix_team_members_organization_id_team_id");
        entity.HasIndex(value => new
        {
            value.TeamId,
            value.OrganizationMemberId
        })
            .IsUnique()
            .HasDatabaseName("ux_team_members_team_id_organization_member_id");
        entity.HasIndex(value => new
        {
            value.TeamId,
            value.JoinedAt,
            value.Id
        })
            .HasDatabaseName("ix_team_members_team_id_joined_at_id");
        entity.HasOne<TeamEntity>()
            .WithMany()
            .HasForeignKey(value => new { value.OrganizationId, value.TeamId })
            .HasPrincipalKey(team => new { team.OrganizationId, team.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(
                "fk_team_members_teams_organization_id_team_id");
        entity.HasOne<OrganizationMemberEntity>()
            .WithMany()
            .HasForeignKey(value => new
            {
                value.OrganizationId,
                value.OrganizationMemberId
            })
            .HasPrincipalKey(member => new
            {
                member.OrganizationId,
                member.Id
            })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(
                "fk_team_members_members_organization_id_organization_member_id");
    }
}
