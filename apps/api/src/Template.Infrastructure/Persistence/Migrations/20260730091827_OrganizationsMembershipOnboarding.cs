using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationsMembershipOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organizations");

            migrationBuilder.AddColumn<Guid>(
                name: "active_organization_id",
                schema: "auth",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                    table.CheckConstraint("ck_organizations_name", "char_length(name) BETWEEN 1 AND 50");
                    table.CheckConstraint("ck_organizations_slug", "char_length(slug) BETWEEN 1 AND 64\nAND slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                });

            migrationBuilder.CreateTable(
                name: "allowed_email_domains",
                schema: "organizations",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allowed_email_domains", x => new { x.organization_id, x.domain });
                    table.CheckConstraint("ck_allowed_email_domains_domain", "char_length(domain) BETWEEN 1 AND 253 AND domain = lower(domain)");
                    table.ForeignKey(
                        name: "fk_allowed_email_domains_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organizations",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "members",
                schema: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.id);
                    table.CheckConstraint("ck_members_role", "role IN ('owner', 'admin', 'member')");
                    table.ForeignKey(
                        name: "fk_members_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organizations",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_active_organization_id",
                schema: "auth",
                table: "sessions",
                column: "active_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_organization_id_joined_at_id",
                schema: "organizations",
                table: "members",
                columns: new[] { "organization_id", "joined_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_members_user_id_organization_id",
                schema: "organizations",
                table: "members",
                columns: new[] { "user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "ux_members_organization_id_user_id",
                schema: "organizations",
                table: "members",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_organizations_slug",
                schema: "organizations",
                table: "organizations",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_organizations_active_organization_id",
                schema: "auth",
                table: "sessions",
                column: "active_organization_id",
                principalSchema: "organizations",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sessions_organizations_active_organization_id",
                schema: "auth",
                table: "sessions");

            migrationBuilder.DropTable(
                name: "allowed_email_domains",
                schema: "organizations");

            migrationBuilder.DropTable(
                name: "members",
                schema: "organizations");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_sessions_active_organization_id",
                schema: "auth",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "active_organization_id",
                schema: "auth",
                table: "sessions");
        }
    }
}
