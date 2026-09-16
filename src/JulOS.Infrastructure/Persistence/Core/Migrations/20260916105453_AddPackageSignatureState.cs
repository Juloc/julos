using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageSignatureState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "signature_state",
                schema: "core",
                table: "package_installations",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_package_installations_signature_state",
                schema: "core",
                table: "package_installations",
                sql: "signature_state IN ('TrustedSigned', 'UnknownSigned', 'NotSigned')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_package_installations_signature_state",
                schema: "core",
                table: "package_installations");

            migrationBuilder.DropColumn(
                name: "signature_state",
                schema: "core",
                table: "package_installations");
        }
    }
}
