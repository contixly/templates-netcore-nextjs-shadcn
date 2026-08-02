using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeysPublicV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "auth",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_start = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    rate_limit_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    rate_limit_window_seconds = table.Column<int>(type: "integer", nullable: false),
                    rate_limit_max = table.Column<int>(type: "integer", nullable: false),
                    window_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    request_count = table.Column<int>(type: "integer", nullable: false),
                    last_request_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rotated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.CheckConstraint("ck_api_keys_exactly_one_owner", "num_nonnulls(user_id, organization_id) = 1");
                    table.CheckConstraint("ck_api_keys_key_hash", "octet_length(key_hash) = 32");
                    table.CheckConstraint("ck_api_keys_name", "char_length(name) BETWEEN 1 AND 32 AND name = btrim(name) AND name !~ '[[:cntrl:]]'");
                    table.CheckConstraint("ck_api_keys_rate_limit_max", "rate_limit_max BETWEEN 1 AND 1000000");
                    table.CheckConstraint("ck_api_keys_rate_limit_window", "rate_limit_window_seconds IN (60, 3600, 86400)");
                    table.CheckConstraint("ck_api_keys_request_count", "request_count >= 0");
                    table.CheckConstraint("ck_api_keys_scopes", "cardinality(scopes) > 0 AND scopes <@ ARRAY['basic:read', 'organization:read', 'member:read', 'team:read', 'teamMember:read']::text[]");
                    table.ForeignKey(
                        name: "fk_api_keys_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organizations",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_organization_id_created_at_id",
                schema: "auth",
                table: "api_keys",
                columns: new[] { "organization_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id_created_at_id",
                schema: "auth",
                table: "api_keys",
                columns: new[] { "user_id", "created_at", "id" },
                descending: new[] { false, true, true },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_api_keys_key_hash",
                schema: "auth",
                table: "api_keys",
                column: "key_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "auth");
        }
    }
}
