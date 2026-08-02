using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;

namespace Template.Infrastructure.ApiKeys;

public sealed class ApiKeyEntityConfiguration : IEntityTypeConfiguration<ApiKeyEntity>
{
    public void Configure(EntityTypeBuilder<ApiKeyEntity> entity)
    {
        entity.ToTable(
            "api_keys",
            "auth",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_api_keys_exactly_one_owner",
                    "num_nonnulls(user_id, organization_id) = 1");
                table.HasCheckConstraint(
                    "ck_api_keys_name",
                    "char_length(name) BETWEEN 1 AND 32 AND name = btrim(name) AND name !~ '[[:cntrl:]]'");
                table.HasCheckConstraint(
                    "ck_api_keys_key_hash",
                    "octet_length(key_hash) = 32");
                table.HasCheckConstraint(
                    "ck_api_keys_key_start",
                    "char_length(key_start) = 16 AND (left(key_start, 5) = 'user_' OR left(key_start, 4) = 'org_') AND key_start !~ '[^A-Za-z0-9_-]'");
                table.HasCheckConstraint(
                    "ck_api_keys_scopes",
                    "cardinality(scopes) > 0 AND scopes <@ ARRAY['basic:read', 'organization:read', 'member:read', 'team:read', 'teamMember:read']::text[]");
                table.HasCheckConstraint(
                    "ck_api_keys_rate_limit_window",
                    "rate_limit_window_seconds IN (60, 3600, 86400)");
                table.HasCheckConstraint(
                    "ck_api_keys_rate_limit_max",
                    "rate_limit_max BETWEEN 1 AND 1000000");
                table.HasCheckConstraint(
                    "ck_api_keys_request_count",
                    "request_count >= 0");
            });
        entity.HasKey(value => value.Id).HasName("pk_api_keys");
        entity.Property(value => value.Id).HasColumnName("id");
        entity.Property(value => value.UserId).HasColumnName("user_id");
        entity.Property(value => value.OrganizationId).HasColumnName("organization_id");
        entity.Property(value => value.Name).HasColumnName("name").HasMaxLength(32);
        entity.Property(value => value.KeyHash).HasColumnName("key_hash").HasColumnType("bytea");
        entity.Property(value => value.KeyStart).HasColumnName("key_start").HasMaxLength(16);
        entity.Property(value => value.Scopes).HasColumnName("scopes").HasColumnType("text[]");
        entity.Property(value => value.Enabled).HasColumnName("enabled");
        entity.Property(value => value.RateLimitEnabled).HasColumnName("rate_limit_enabled");
        entity.Property(value => value.RateLimitWindowSeconds)
            .HasColumnName("rate_limit_window_seconds");
        entity.Property(value => value.RateLimitMax).HasColumnName("rate_limit_max");
        entity.Property(value => value.WindowStartedAt)
            .HasColumnName("window_started_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.RequestCount).HasColumnName("request_count");
        entity.Property(value => value.LastRequestAt)
            .HasColumnName("last_request_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.RotatedAt)
            .HasColumnName("rotated_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");
        entity.Property(value => value.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");
        entity.HasIndex(value => value.KeyHash)
            .IsUnique()
            .HasDatabaseName("ux_api_keys_key_hash");
        entity.HasIndex(value => new { value.UserId, value.CreatedAt, value.Id })
            .IsDescending(false, true, true)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_api_keys_user_id_created_at_id");
        entity.HasIndex(value => new
        {
            value.OrganizationId,
            value.CreatedAt,
            value.Id
        })
            .IsDescending(false, true, true)
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_api_keys_organization_id_created_at_id");
        entity.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_api_keys_users_user_id");
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(value => value.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_api_keys_organizations_organization_id");
    }
}
