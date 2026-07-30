using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountSessionAuthenticationMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "authentication_method",
                schema: "auth",
                table: "sessions",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "local");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sessions_authentication_method",
                schema: "auth",
                table: "sessions",
                sql: "authentication_method IN (\n    'local',\n    'google',\n    'github',\n    'gitlab',\n    'vk',\n    'yandex')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sessions_authentication_method",
                schema: "auth",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "authentication_method",
                schema: "auth",
                table: "sessions");
        }
    }
}
