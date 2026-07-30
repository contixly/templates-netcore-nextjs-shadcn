using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountsExternalOAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "openiddict_applications",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_secret = table.Column<string>(type: "text", nullable: true),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "openiddict_scopes",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_scopes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_emails",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_emails", x => x.id);
                    table.CheckConstraint("ck_user_emails_email", "char_length(email) BETWEEN 1 AND 254");
                    table.CheckConstraint("ck_user_emails_normalized_email", "char_length(normalized_email) BETWEEN 1 AND 254");
                    table.ForeignKey(
                        name: "fk_user_emails_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM auth.users
                        WHERE email IS NULL
                           OR normalized_email IS NULL
                           OR email = ''
                           OR normalized_email = ''
                    ) THEN
                        RAISE EXCEPTION 'Cannot backfill verified email for an Identity user without email data.';
                    END IF;
                END
                $$;

                INSERT INTO auth.user_emails (
                    id,
                    user_id,
                    email,
                    normalized_email,
                    is_primary,
                    created_at)
                SELECT
                    gen_random_uuid(),
                    id,
                    email,
                    normalized_email,
                    TRUE,
                    created_at
                FROM auth.users;
                """);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "connected_at",
                schema: "auth",
                table: "user_logins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_used_at",
                schema: "auth",
                table: "user_logins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verified_email_id",
                schema: "auth",
                table: "user_logins",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE auth.user_logins AS login
                SET
                    verified_email_id = email.id,
                    connected_at = users.created_at
                FROM auth.user_emails AS email
                INNER JOIN auth.users AS users
                    ON users.id = email.user_id
                WHERE login.user_id = email.user_id
                  AND email.is_primary;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "connected_at",
                schema: "auth",
                table: "user_logins",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "verified_email_id",
                schema: "auth",
                table: "user_logins",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "openiddict_authorizations",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_authorizations", x => x.id);
                    table.ForeignKey(
                        name: "FK_openiddict_authorizations_openiddict_applications_applicati~",
                        column: x => x.application_id,
                        principalSchema: "auth",
                        principalTable: "openiddict_applications",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "openiddict_tokens",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    application_id = table.Column<string>(type: "text", nullable: true),
                    authorization_id = table.Column<string>(type: "text", nullable: true),
                    concurrency_token = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_openiddict_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_openiddict_tokens_openiddict_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "auth",
                        principalTable: "openiddict_applications",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_openiddict_tokens_openiddict_authorizations_authorization_id",
                        column: x => x.authorization_id,
                        principalSchema: "auth",
                        principalTable: "openiddict_authorizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_verified_email_id",
                schema: "auth",
                table: "user_logins",
                column: "verified_email_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_logins_user_provider",
                schema: "auth",
                table: "user_logins",
                columns: new[] { "user_id", "login_provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_applications_client_id",
                schema: "auth",
                table: "openiddict_applications",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_authorizations_application_id_status_subject_type",
                schema: "auth",
                table: "openiddict_authorizations",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_scopes_name",
                schema: "auth",
                table: "openiddict_scopes",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_tokens_application_id_status_subject_type",
                schema: "auth",
                table: "openiddict_tokens",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_tokens_authorization_id",
                schema: "auth",
                table: "openiddict_tokens",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "IX_openiddict_tokens_reference_id",
                schema: "auth",
                table: "openiddict_tokens",
                column: "reference_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_emails_user_id",
                schema: "auth",
                table: "user_emails",
                columns: new[] { "user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_user_emails_normalized_email",
                schema: "auth",
                table: "user_emails",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_emails_primary_user_id",
                schema: "auth",
                table: "user_emails",
                column: "user_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.AddForeignKey(
                name: "fk_user_logins_user_emails_verified_email_id",
                schema: "auth",
                table: "user_logins",
                column: "verified_email_id",
                principalSchema: "auth",
                principalTable: "user_emails",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_user_logins_user_emails_verified_email_id",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "openiddict_scopes",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "openiddict_tokens",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "user_emails",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "openiddict_authorizations",
                schema: "auth");

            migrationBuilder.DropTable(
                name: "openiddict_applications",
                schema: "auth");

            migrationBuilder.DropIndex(
                name: "ix_user_logins_verified_email_id",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropIndex(
                name: "ux_user_logins_user_provider",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropColumn(
                name: "connected_at",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropColumn(
                name: "last_used_at",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropColumn(
                name: "verified_email_id",
                schema: "auth",
                table: "user_logins");
        }
    }
}
