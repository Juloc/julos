using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddClientDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "client_devices",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_instance_key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_detected_workspace_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    workspace_class_override = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_devices", x => x.id);
                    table.CheckConstraint("ck_client_devices_override", "workspace_class_override IS NULL OR workspace_class_override IN ('Phone', 'Tablet', 'DesktopSingle')");
                    table.CheckConstraint("ck_client_devices_revision", "revision >= 1");
                    table.CheckConstraint("ck_client_devices_seen", "last_seen_at_utc >= created_at_utc");
                    table.CheckConstraint("ck_client_devices_workspace", "last_detected_workspace_class IN ('Phone', 'Tablet', 'DesktopSingle')");
                });

            migrationBuilder.CreateTable(
                name: "application_execution_preferences",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    client_device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    background_mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_execution_preferences", x => x.id);
                    table.CheckConstraint("ck_application_execution_preferences_mode", "background_mode IN ('Suspend', 'KeepSurfaceActive')");
                    table.CheckConstraint("ck_application_execution_preferences_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "fk_application_execution_preferences_device",
                        column: x => x.client_device_id,
                        principalSchema: "core",
                        principalTable: "client_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_workspace_preferences",
                schema: "core",
                columns: table => new
                {
                    client_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    layout_scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    restore_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_workspace_preferences", x => new { x.client_device_id, x.workspace_class });
                    table.CheckConstraint("ck_device_workspace_preferences_restore", "restore_mode IN ('Resume', 'Fresh')");
                    table.CheckConstraint("ck_device_workspace_preferences_scope", "layout_scope IN ('Shared', 'Device')");
                    table.ForeignKey(
                        name: "fk_device_workspace_preferences_device",
                        column: x => x.client_device_id,
                        principalSchema: "core",
                        principalTable: "client_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_execution_preferences_client_device_id",
                schema: "core",
                table: "application_execution_preferences",
                column: "client_device_id");

            migrationBuilder.CreateIndex(
                name: "ux_application_execution_preferences_device",
                schema: "core",
                table: "application_execution_preferences",
                columns: new[] { "owner_user_id", "application_definition_id", "workspace_class", "client_device_id" },
                unique: true,
                filter: "client_device_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_application_execution_preferences_shared",
                schema: "core",
                table: "application_execution_preferences",
                columns: new[] { "owner_user_id", "application_definition_id", "workspace_class" },
                unique: true,
                filter: "client_device_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_client_devices_owner",
                schema: "core",
                table: "client_devices",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_client_devices_key_hash",
                schema: "core",
                table: "client_devices",
                column: "client_instance_key_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_execution_preferences",
                schema: "core");

            migrationBuilder.DropTable(
                name: "device_workspace_preferences",
                schema: "core");

            migrationBuilder.DropTable(
                name: "client_devices",
                schema: "core");
        }
    }
}
