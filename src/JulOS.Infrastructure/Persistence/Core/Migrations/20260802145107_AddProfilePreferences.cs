using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_language",
                schema: "core",
                table: "users");

            migrationBuilder.AddColumn<string>(
                name: "motion",
                schema: "core",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "enabled");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_language",
                schema: "core",
                table: "users",
                sql: "preferred_language IN ('en', 'de')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_motion",
                schema: "core",
                table: "users",
                sql: "motion IN ('enabled', 'reduced')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_language",
                schema: "core",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_motion",
                schema: "core",
                table: "users");

            migrationBuilder.DropColumn(
                name: "motion",
                schema: "core",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_language",
                schema: "core",
                table: "users",
                sql: "char_length(preferred_language) BETWEEN 2 AND 32");
        }
    }
}
