using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalog_sources",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    location = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    authentication_secret_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trust_level = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_successful_revision = table.Column<int>(type: "integer", nullable: true),
                    last_successful_digest = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_refresh_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_refresh_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    last_failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_sources", x => x.id);
                    table.CheckConstraint("ck_catalog_sources_kind", "kind IN ('Official', 'Https', 'Git', 'Oci', 'Local')");
                    table.CheckConstraint("ck_catalog_sources_refresh_evidence", "(last_refresh_state = 'Never' AND last_successful_revision IS NULL) OR (last_refresh_state <> 'Never' AND last_successful_revision IS NOT NULL AND last_successful_digest IS NOT NULL)");
                    table.CheckConstraint("ck_catalog_sources_refresh_state", "last_refresh_state IN ('Never', 'Fresh', 'Stale')");
                    table.CheckConstraint("ck_catalog_sources_revision", "revision >= 1");
                    table.CheckConstraint("ck_catalog_sources_trust_level", "(kind = 'Official' AND trust_level = 'Official') OR (kind <> 'Official' AND trust_level IN ('AdministratorTrusted', 'Custom'))");
                });

            migrationBuilder.CreateTable(
                name: "catalog_publisher_keys",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    key_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    public_key_spki = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    public_key_fingerprint = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    administrator_trust_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    administrator_decision_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    administrator_decision_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_observed_source_revision = table.Column<int>(type: "integer", nullable: false),
                    last_observed_source_revision = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_publisher_keys", x => x.id);
                    table.CheckConstraint("ck_catalog_publisher_keys_decision", "(administrator_trust_state = 'Unknown' AND administrator_decision_by_user_id IS NULL AND administrator_decision_at_utc IS NULL) OR (administrator_trust_state <> 'Unknown' AND administrator_decision_by_user_id IS NOT NULL AND administrator_decision_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_catalog_publisher_keys_revision", "revision >= 1");
                    table.CheckConstraint("ck_catalog_publisher_keys_trust", "administrator_trust_state IN ('Unknown', 'Trusted', 'Distrusted')");
                    table.CheckConstraint("ck_catalog_publisher_keys_validity", "valid_until_utc IS NULL OR valid_until_utc > valid_from_utc");
                    table.ForeignKey(
                        name: "fk_catalog_publisher_keys_source",
                        column: x => x.catalog_source_id,
                        principalSchema: "core",
                        principalTable: "catalog_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_publisher_keys_identity",
                schema: "core",
                table: "catalog_publisher_keys",
                columns: new[] { "catalog_source_id", "publisher_id", "key_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_catalog_sources_location",
                schema: "core",
                table: "catalog_sources",
                column: "location",
                unique: true,
                filter: "deleted_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_publisher_keys",
                schema: "core");

            migrationBuilder.DropTable(
                name: "catalog_sources",
                schema: "core");
        }
    }
}
