using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationSurfaceDeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "surface_contract_version",
                schema: "core",
                table: "application_definitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "surface_handles_back",
                schema: "core",
                table: "application_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "surface_supports_keep_active",
                schema: "core",
                table: "application_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "surface_contract_version",
                schema: "core",
                table: "application_definitions");

            migrationBuilder.DropColumn(
                name: "surface_handles_back",
                schema: "core",
                table: "application_definitions");

            migrationBuilder.DropColumn(
                name: "surface_supports_keep_active",
                schema: "core",
                table: "application_definitions");
        }
    }
}
