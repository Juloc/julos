using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogEntryCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_identity",
                schema: "core",
                table: "catalog_sources",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalog_entry_cache",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    source_digest = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    signature_key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    public_key_fingerprint = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    signature_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    trust_assessment_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cached_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_entry_cache", x => x.id);
                    table.CheckConstraint("ck_catalog_entry_cache_revision", "revision >= 1");
                    table.CheckConstraint("ck_catalog_entry_cache_signature_state", "signature_state IN ('TrustedSigned', 'UnknownSigned', 'NotSigned', 'InvalidSignature')");
                    table.CheckConstraint("ck_catalog_entry_cache_signer", "(signature_state = 'NotSigned' AND publisher_id IS NULL AND signature_key_id IS NULL AND public_key_fingerprint IS NULL) OR (signature_state <> 'NotSigned' AND publisher_id IS NOT NULL AND signature_key_id IS NOT NULL AND public_key_fingerprint IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_catalog_entry_cache_source",
                        column: x => x.catalog_source_id,
                        principalSchema: "core",
                        principalTable: "catalog_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_entry_cache_identity",
                schema: "core",
                table: "catalog_entry_cache",
                columns: new[] { "catalog_source_id", "app_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_entry_cache",
                schema: "core");

            migrationBuilder.DropColumn(
                name: "source_identity",
                schema: "core",
                table: "catalog_sources");
        }
    }
}
