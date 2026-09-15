using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceViewportLayoutIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_desktop_windows_layout",
                schema: "core",
                table: "desktop_windows");

            migrationBuilder.DropIndex(
                name: "ux_desktop_layouts_default_per_viewport",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropIndex(
                name: "ux_desktop_layouts_user_viewport_name",
                schema: "core",
                table: "desktop_layouts");

            // Only the default layout per viewport was ever reachable through the API, and
            // the new identity has no room for a second one. Any other row is removed before
            // the flag that distinguished them disappears, rather than silently colliding
            // with the unique index added below.
            migrationBuilder.Sql("DELETE FROM core.desktop_layouts WHERE NOT is_default;");

            migrationBuilder.DropColumn(
                name: "is_default",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.RenameColumn(
                name: "viewport_class",
                schema: "core",
                table: "desktop_layouts",
                newName: "workspace_class");

            migrationBuilder.AddColumn<int>(
                name: "display_slot",
                schema: "core",
                table: "desktop_windows",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "workspace_class",
                schema: "core",
                table: "desktop_windows",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "client_device_id",
                schema: "core",
                table: "desktop_layouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "display_count",
                schema: "core",
                table: "desktop_layouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "presentation_mode",
                schema: "core",
                table: "desktop_layouts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "primary_window_id",
                schema: "core",
                table: "desktop_layouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "secondary_window_id",
                schema: "core",
                table: "desktop_layouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "split_ratio_permille",
                schema: "core",
                table: "desktop_layouts",
                type: "integer",
                nullable: true);

            // The viewport vocabulary becomes the workspace vocabulary. Every window, its
            // bounds, its z-order and the layout revision are kept exactly as they are; only
            // the identity of the layout they belong to is renamed.
            migrationBuilder.Sql(
                """
                UPDATE core.desktop_layouts SET workspace_class = 'DesktopSingle' WHERE workspace_class = 'Desktop';
                UPDATE core.desktop_layouts SET workspace_class = 'Phone' WHERE workspace_class = 'Mobile';
                UPDATE core.desktop_layouts SET display_count = 1;
                UPDATE core.desktop_layouts SET presentation_mode = 'Freeform' WHERE workspace_class <> 'Phone';
                """);

            // Each window carries a copy of its parent's class from here on, which is what
            // lets the database refuse a cross-class write later.
            migrationBuilder.Sql(
                """
                UPDATE core.desktop_windows AS w
                SET workspace_class = l.workspace_class
                FROM core.desktop_layouts AS l
                WHERE w.desktop_layout_id = l.id;
                """);

            // A migrated phone keeps every window. The foreground one is chosen
            // deterministically as the non-minimized window nearest the front, ties broken by
            // identity, so migrating the same database twice produces the same result. Split
            // is never produced here: showing two windows at once is an explicit user action
            // and must not be inferred from an old layout that merely held several windows.
            migrationBuilder.Sql(
                """
                UPDATE core.desktop_layouts AS l
                SET presentation_mode = 'PhoneSingle', primary_window_id = chosen.id
                FROM (
                    SELECT DISTINCT ON (w.desktop_layout_id) w.desktop_layout_id, w.id
                    FROM core.desktop_windows AS w
                    WHERE w.state <> 'Minimized'
                    ORDER BY w.desktop_layout_id, w.z_index DESC, w.id
                ) AS chosen
                WHERE l.id = chosen.desktop_layout_id AND l.workspace_class = 'Phone';

                UPDATE core.desktop_layouts
                SET presentation_mode = 'PhoneEmpty'
                WHERE workspace_class = 'Phone' AND presentation_mode <> 'PhoneSingle';
                """);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_desktop_layouts_id_workspace_class",
                schema: "core",
                table: "desktop_layouts",
                columns: new[] { "id", "workspace_class" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_client_devices_owner_device",
                schema: "core",
                table: "client_devices",
                columns: new[] { "owner_user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_desktop_windows_desktop_layout_id_workspace_class",
                schema: "core",
                table: "desktop_windows",
                columns: new[] { "desktop_layout_id", "workspace_class" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_desktop_windows_display_slot",
                schema: "core",
                table: "desktop_windows",
                sql: "display_slot >= 0 AND (workspace_class = 'DesktopMulti' OR display_slot = 0)");

            migrationBuilder.CreateIndex(
                name: "ux_desktop_layouts_device",
                schema: "core",
                table: "desktop_layouts",
                columns: new[] { "user_id", "client_device_id", "workspace_class" },
                unique: true,
                filter: "client_device_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_desktop_layouts_shared",
                schema: "core",
                table: "desktop_layouts",
                columns: new[] { "user_id", "workspace_class" },
                unique: true,
                filter: "client_device_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desktop_layouts_display_count",
                schema: "core",
                table: "desktop_layouts",
                sql: "display_count >= 1 AND (workspace_class = 'DesktopMulti' OR display_count = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desktop_layouts_mode_class",
                schema: "core",
                table: "desktop_layouts",
                sql: "(workspace_class = 'Phone' AND presentation_mode IN ('PhoneEmpty', 'PhoneSingle', 'PhoneSplit')) OR (workspace_class <> 'Phone' AND presentation_mode IN ('Freeform', 'Tiled'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desktop_layouts_presentation_state",
                schema: "core",
                table: "desktop_layouts",
                sql: "(presentation_mode IN ('Freeform', 'Tiled', 'PhoneEmpty') AND primary_window_id IS NULL AND secondary_window_id IS NULL AND split_ratio_permille IS NULL) OR (presentation_mode = 'PhoneSingle' AND primary_window_id IS NOT NULL AND secondary_window_id IS NULL AND split_ratio_permille IS NULL) OR (presentation_mode = 'PhoneSplit' AND primary_window_id IS NOT NULL AND secondary_window_id IS NOT NULL AND primary_window_id <> secondary_window_id AND split_ratio_permille BETWEEN 250 AND 750)");

            migrationBuilder.AddForeignKey(
                name: "fk_desktop_layouts_device",
                schema: "core",
                table: "desktop_layouts",
                columns: new[] { "user_id", "client_device_id" },
                principalSchema: "core",
                principalTable: "client_devices",
                principalColumns: new[] { "owner_user_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_desktop_windows_layout",
                schema: "core",
                table: "desktop_windows",
                columns: new[] { "desktop_layout_id", "workspace_class" },
                principalSchema: "core",
                principalTable: "desktop_layouts",
                principalColumns: new[] { "id", "workspace_class" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_desktop_layouts_device",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropForeignKey(
                name: "fk_desktop_windows_layout",
                schema: "core",
                table: "desktop_windows");

            migrationBuilder.DropIndex(
                name: "IX_desktop_windows_desktop_layout_id_workspace_class",
                schema: "core",
                table: "desktop_windows");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desktop_windows_display_slot",
                schema: "core",
                table: "desktop_windows");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_desktop_layouts_id_workspace_class",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropIndex(
                name: "ux_desktop_layouts_device",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropIndex(
                name: "ux_desktop_layouts_shared",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desktop_layouts_display_count",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desktop_layouts_mode_class",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desktop_layouts_presentation_state",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_client_devices_owner_device",
                schema: "core",
                table: "client_devices");

            migrationBuilder.DropColumn(
                name: "display_slot",
                schema: "core",
                table: "desktop_windows");

            migrationBuilder.DropColumn(
                name: "workspace_class",
                schema: "core",
                table: "desktop_windows");

            migrationBuilder.DropColumn(
                name: "client_device_id",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropColumn(
                name: "display_count",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropColumn(
                name: "presentation_mode",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropColumn(
                name: "primary_window_id",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropColumn(
                name: "secondary_window_id",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.DropColumn(
                name: "split_ratio_permille",
                schema: "core",
                table: "desktop_layouts");

            migrationBuilder.RenameColumn(
                name: "workspace_class",
                schema: "core",
                table: "desktop_layouts",
                newName: "viewport_class");

            migrationBuilder.AddColumn<bool>(
                name: "is_default",
                schema: "core",
                table: "desktop_layouts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ux_desktop_layouts_default_per_viewport",
                schema: "core",
                table: "desktop_layouts",
                columns: new[] { "user_id", "viewport_class" },
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_desktop_layouts_user_viewport_name",
                schema: "core",
                table: "desktop_layouts",
                columns: new[] { "user_id", "viewport_class", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_desktop_windows_layout",
                schema: "core",
                table: "desktop_windows",
                column: "desktop_layout_id",
                principalSchema: "core",
                principalTable: "desktop_layouts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
