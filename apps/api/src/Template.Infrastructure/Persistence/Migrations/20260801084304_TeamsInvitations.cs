using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamsInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_members_organization_id_id",
                schema: "organizations",
                table: "members",
                columns: new[] { "organization_id", "id" });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.UniqueConstraint("ak_teams_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_teams_name", "char_length(name) BETWEEN 1 AND 50\nAND name = btrim(name)\nAND name ~ '^[[:alnum:] _-]+$'");
                    table.ForeignKey(
                        name: "fk_teams_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organizations",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    role = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    status = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    inviter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.CheckConstraint("ck_invitations_email", "char_length(email) BETWEEN 1 AND 254 AND email = lower(email)");
                    table.CheckConstraint("ck_invitations_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_invitations_role", "role IN ('owner', 'admin', 'member')");
                    table.CheckConstraint("ck_invitations_status", "status IN ('pending', 'accepted', 'rejected', 'canceled')");
                    table.ForeignKey(
                        name: "fk_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organizations",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitations_teams_organization_id_team_id",
                        columns: x => new { x.organization_id, x.team_id },
                        principalSchema: "organizations",
                        principalTable: "teams",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invitations_users_inviter_user_id",
                        column: x => x.inviter_user_id,
                        principalSchema: "auth",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                schema: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_team_members_members_organization_id_organization_member_id",
                        columns: x => new { x.organization_id, x.organization_member_id },
                        principalSchema: "organizations",
                        principalTable: "members",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_members_teams_organization_id_team_id",
                        columns: x => new { x.organization_id, x.team_id },
                        principalSchema: "organizations",
                        principalTable: "teams",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_email_status_expires_at_created_at_id",
                schema: "organizations",
                table: "invitations",
                columns: new[] { "email", "status", "expires_at", "created_at", "id" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_inviter_user_id",
                schema: "organizations",
                table: "invitations",
                column: "inviter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_organization_id_created_at_id",
                schema: "organizations",
                table: "invitations",
                columns: new[] { "organization_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_organization_id_team_id",
                schema: "organizations",
                table: "invitations",
                columns: new[] { "organization_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_organization_inviter_status_expires_at",
                schema: "organizations",
                table: "invitations",
                columns: new[] { "organization_id", "inviter_user_id", "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_invitations_organization_id_email_pending",
                schema: "organizations",
                table: "invitations",
                columns: new[] { "organization_id", "email" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_team_members_organization_id_organization_member_id",
                schema: "organizations",
                table: "team_members",
                columns: new[] { "organization_id", "organization_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_members_organization_id_team_id",
                schema: "organizations",
                table: "team_members",
                columns: new[] { "organization_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_members_team_id_joined_at_id",
                schema: "organizations",
                table: "team_members",
                columns: new[] { "team_id", "joined_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_team_members_team_id_organization_member_id",
                schema: "organizations",
                table: "team_members",
                columns: new[] { "team_id", "organization_member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_organization_id_created_at_id",
                schema: "organizations",
                table: "teams",
                columns: new[] { "organization_id", "created_at", "id" });

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_teams_organization_id_lower_name
                ON organizations.teams (organization_id, lower(name));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX organizations.ux_teams_organization_id_lower_name;
                """);

            migrationBuilder.DropTable(
                name: "invitations",
                schema: "organizations");

            migrationBuilder.DropTable(
                name: "team_members",
                schema: "organizations");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "organizations");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_members_organization_id_id",
                schema: "organizations",
                table: "members");
        }
    }
}
