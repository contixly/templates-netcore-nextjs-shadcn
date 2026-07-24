using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Template.Infrastructure.Identity;

namespace Template.Infrastructure.Persistence;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public const string Schema = "auth";

    public DbSet<AuthSessionEntity> Sessions => Set<AuthSessionEntity>();

    public static void Configure(
        DbContextOptionsBuilder options,
        string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserName).HasColumnName("user_name").HasMaxLength(254);
            entity.Property(value => value.NormalizedUserName)
                .HasColumnName("normalized_user_name")
                .HasMaxLength(254);
            entity.Property(value => value.Email).HasColumnName("email").HasMaxLength(254);
            entity.Property(value => value.NormalizedEmail)
                .HasColumnName("normalized_email")
                .HasMaxLength(254);
            entity.Property(value => value.EmailConfirmed).HasColumnName("email_confirmed");
            entity.Property(value => value.PasswordHash).HasColumnName("password_hash");
            entity.Property(value => value.SecurityStamp).HasColumnName("security_stamp");
            entity.Property(value => value.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            entity.Property(value => value.PhoneNumber).HasColumnName("phone_number");
            entity.Property(value => value.PhoneNumberConfirmed)
                .HasColumnName("phone_number_confirmed");
            entity.Property(value => value.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            entity.Property(value => value.LockoutEnd).HasColumnName("lockout_end");
            entity.Property(value => value.LockoutEnabled).HasColumnName("lockout_enabled");
            entity.Property(value => value.AccessFailedCount).HasColumnName("access_failed_count");
            entity.Property(value => value.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(value => value.ImageUrl)
                .HasColumnName("image_url")
                .HasMaxLength(2048);
            entity.Property(value => value.IsLocalAutomation)
                .HasColumnName("is_local_automation");
            entity.Property(value => value.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(value => value.NormalizedUserName)
                .IsUnique()
                .HasDatabaseName("ux_users_normalized_user_name");
            entity.HasIndex(value => value.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("ux_users_normalized_email");
        });

        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("user_claims");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.ClaimType).HasColumnName("claim_type");
            entity.Property(value => value.ClaimValue).HasColumnName("claim_value");
            entity.HasIndex(value => value.UserId).HasDatabaseName("ix_user_claims_user_id");
        });

        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("user_logins");
            entity.Property(value => value.LoginProvider).HasColumnName("login_provider");
            entity.Property(value => value.ProviderKey).HasColumnName("provider_key");
            entity.Property(value => value.ProviderDisplayName).HasColumnName("provider_display_name");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.HasIndex(value => value.UserId).HasDatabaseName("ix_user_logins_user_id");
        });

        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("user_tokens");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.LoginProvider).HasColumnName("login_provider");
            entity.Property(value => value.Name).HasColumnName("name");
            entity.Property(value => value.Value).HasColumnName("value");
        });

        builder.Entity<AuthSessionEntity>(entity =>
        {
            entity.ToTable("sessions", table =>
                table.HasCheckConstraint(
                    "ck_sessions_expiry",
                    "expires_at > created_at"));
            entity.HasKey(value => value.Id).HasName("pk_sessions");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.TicketKeyHash)
                .HasColumnName("ticket_key_hash")
                .HasColumnType("bytea");
            entity.Property(value => value.ProtectedTicket)
                .HasColumnName("protected_ticket")
                .HasColumnType("bytea");
            entity.Property(value => value.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.ExpiresAt)
                .HasColumnName("expires_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.IpAddress)
                .HasColumnName("ip_address")
                .HasColumnType("inet");
            entity.Property(value => value.UserAgent)
                .HasColumnName("user_agent")
                .HasMaxLength(512);
            entity.HasIndex(value => value.TicketKeyHash)
                .IsUnique()
                .HasDatabaseName("ux_sessions_ticket_key_hash");
            entity.HasIndex(value => value.UserId)
                .HasDatabaseName("ix_sessions_user_id");
            entity.HasIndex(value => value.ExpiresAt)
                .HasDatabaseName("ix_sessions_expires_at");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_sessions_users_user_id");
        });
    }
}
