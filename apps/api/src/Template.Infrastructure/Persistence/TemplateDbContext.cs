using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using Template.Infrastructure.Identity;

namespace Template.Infrastructure.Persistence;

public sealed class TemplateDbContext(DbContextOptions<TemplateDbContext> options)
    : IdentityUserContext<
        ApplicationUser,
        Guid,
        IdentityUserClaim<Guid>,
        ApplicationUserLogin,
        IdentityUserToken<Guid>>(options),
      IDataProtectionKeyContext
{
    public const string Schema = "auth";

    public DbSet<AuthSessionEntity> Sessions => Set<AuthSessionEntity>();
    public DbSet<UserEmailEntity> UserEmails => Set<UserEmailEntity>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<OpenIddictEntityFrameworkCoreToken> OpenIddictTokens =>
        Set<OpenIddictEntityFrameworkCoreToken>();

    public static void Configure(
        DbContextOptionsBuilder options,
        string connectionString) =>
        options
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
            .UseOpenIddict();

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

        builder.Entity<ApplicationUserLogin>(entity =>
        {
            entity.ToTable("user_logins");
            entity.Property(value => value.LoginProvider).HasColumnName("login_provider");
            entity.Property(value => value.ProviderKey).HasColumnName("provider_key");
            entity.Property(value => value.ProviderDisplayName).HasColumnName("provider_display_name");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.VerifiedEmailId)
                .HasColumnName("verified_email_id");
            entity.Property(value => value.ConnectedAt)
                .HasColumnName("connected_at")
                .HasColumnType("timestamp with time zone");
            entity.Property(value => value.LastUsedAt)
                .HasColumnName("last_used_at")
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(value => value.UserId).HasDatabaseName("ix_user_logins_user_id");
            entity.HasIndex(value => new { value.UserId, value.LoginProvider })
                .IsUnique()
                .HasDatabaseName("ux_user_logins_user_provider");
            entity.HasIndex(value => value.VerifiedEmailId)
                .HasDatabaseName("ix_user_logins_verified_email_id");
            entity.HasOne<UserEmailEntity>()
                .WithMany()
                .HasForeignKey(value => value.VerifiedEmailId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_logins_user_emails_verified_email_id");
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
            entity.ToTable(
                "sessions",
                table =>
                {
                    table.HasCheckConstraint(
                        "ck_sessions_expiry",
                        "expires_at > created_at");
                    table.HasCheckConstraint(
                        "ck_sessions_authentication_method",
                        """
                        authentication_method IN (
                            'local',
                            'google',
                            'github',
                            'gitlab',
                            'vk',
                            'yandex')
                        """);
                });
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
            entity.Property(value => value.AuthenticationMethod)
                .HasColumnName("authentication_method")
                .HasMaxLength(6)
                .HasDefaultValue("local");
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

        builder.Entity<UserEmailEntity>(entity =>
        {
            entity.ToTable(
                "user_emails",
                table =>
                {
                    table.HasCheckConstraint(
                        "ck_user_emails_email",
                        "char_length(email) BETWEEN 1 AND 254");
                    table.HasCheckConstraint(
                        "ck_user_emails_normalized_email",
                        "char_length(normalized_email) BETWEEN 1 AND 254");
                });
            entity.HasKey(value => value.Id).HasName("pk_user_emails");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.Email)
                .HasColumnName("email")
                .HasMaxLength(254);
            entity.Property(value => value.NormalizedEmail)
                .HasColumnName("normalized_email")
                .HasMaxLength(254);
            entity.Property(value => value.IsPrimary).HasColumnName("is_primary");
            entity.Property(value => value.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(value => value.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("ux_user_emails_normalized_email");
            entity.HasIndex(value => new { value.UserId, value.Id })
                .HasDatabaseName("ix_user_emails_user_id");
            entity.HasIndex(value => value.UserId)
                .IsUnique()
                .HasFilter("is_primary")
                .HasDatabaseName("ux_user_emails_primary_user_id");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(value => value.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_user_emails_users_user_id");
        });

        builder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("data_protection_keys");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.FriendlyName)
                .HasColumnName("friendly_name");
            entity.Property(value => value.Xml).HasColumnName("xml");
        });

        builder.UseOpenIddict();
        ConfigureOpenIddict(builder);
    }

    private static void ConfigureOpenIddict(ModelBuilder builder)
    {
        builder.Entity<OpenIddictEntityFrameworkCoreApplication>(entity =>
        {
            entity.ToTable("openiddict_applications");
            UseSnakeCaseProperties(entity);
        });
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization>(entity =>
        {
            entity.ToTable("openiddict_authorizations");
            UseSnakeCaseProperties(entity);
        });
        builder.Entity<OpenIddictEntityFrameworkCoreScope>(entity =>
        {
            entity.ToTable("openiddict_scopes");
            UseSnakeCaseProperties(entity);
        });
        builder.Entity<OpenIddictEntityFrameworkCoreToken>(entity =>
        {
            entity.ToTable("openiddict_tokens");
            UseSnakeCaseProperties(entity);
        });
    }

    private static void UseSnakeCaseProperties<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        foreach (var property in entity.Metadata.GetProperties())
        {
            property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string value) =>
        string.Concat(value.Select((character, index) =>
            index > 0 && char.IsUpper(character)
                ? $"_{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));
}
