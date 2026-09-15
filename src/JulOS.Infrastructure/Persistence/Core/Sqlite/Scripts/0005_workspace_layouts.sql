-- 0005_workspace_layouts
-- Replace viewport-only layout identity with workspace class and layout scope
-- (MOB-004, docs/MOBILE_PWA.md sections 4 and 5).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model changes
-- and add a new migration instead of editing an applied one.
--
-- Every window, its bounds, its z-order and every layout revision survive unchanged. Only
-- the identity of the layout they belong to is translated:
--
--     desktop -> shared desktop-single
--     tablet  -> shared tablet
--     mobile  -> shared phone
--
-- No multi-display topology is invented here: the first desktop-multi layout is created
-- explicitly by its owner, never inferred from stored geometry.

-- Only the default layout per viewport was ever reachable through the API, and the new
-- identity has no room for a second one. Any other row is removed before the flag that
-- distinguished them disappears, rather than silently colliding with the unique index.
DELETE FROM "desktop_layouts" WHERE "is_default" = 0;

-- client_devices: layouts reference a device together with its owner, which needs the
-- pair to be unique on the parent.
CREATE TABLE "client_devices__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_client_devices" PRIMARY KEY,
    "owner_user_id" TEXT NOT NULL,
    "client_instance_key_hash" TEXT NOT NULL,
    "display_name" TEXT NOT NULL,
    "last_detected_workspace_class" TEXT NOT NULL,
    "workspace_class_override" TEXT NULL,
    "created_at_utc" TEXT NOT NULL,
    "last_seen_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ak_client_devices_owner_device" UNIQUE ("owner_user_id", "id"),
    CONSTRAINT "ck_client_devices_override" CHECK (workspace_class_override IS NULL OR workspace_class_override IN ('Phone', 'Tablet', 'DesktopSingle')),
    CONSTRAINT "ck_client_devices_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_client_devices_seen" CHECK (last_seen_at_utc >= created_at_utc),
    CONSTRAINT "ck_client_devices_workspace" CHECK (last_detected_workspace_class IN ('Phone', 'Tablet', 'DesktopSingle'))
);
INSERT INTO "client_devices__julos_new" SELECT * FROM "client_devices";
DROP TABLE "client_devices";
ALTER TABLE "client_devices__julos_new" RENAME TO "client_devices";
CREATE INDEX "ix_client_devices_owner" ON "client_devices" ("owner_user_id");
CREATE UNIQUE INDEX "ux_client_devices_key_hash" ON "client_devices" ("client_instance_key_hash");

-- desktop_layouts
CREATE TABLE "desktop_layouts__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_desktop_layouts" PRIMARY KEY,
    "user_id" TEXT NOT NULL,
    "workspace_class" TEXT NOT NULL,
    "client_device_id" TEXT NULL,
    "name" TEXT NOT NULL,
    "presentation_mode" TEXT NOT NULL,
    "primary_window_id" TEXT NULL,
    "secondary_window_id" TEXT NULL,
    "split_ratio_permille" INTEGER NULL,
    "display_count" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    "updated_at_utc" TEXT NOT NULL,
    CONSTRAINT "ak_desktop_layouts_id_workspace_class" UNIQUE ("id", "workspace_class"),
    CONSTRAINT "ck_desktop_layouts_display_count" CHECK (display_count >= 1 AND (workspace_class = 'DesktopMulti' OR display_count = 1)),
    CONSTRAINT "ck_desktop_layouts_mode_class" CHECK ((workspace_class = 'Phone' AND presentation_mode IN ('PhoneEmpty', 'PhoneSingle', 'PhoneSplit')) OR (workspace_class <> 'Phone' AND presentation_mode IN ('Freeform', 'Tiled'))),
    CONSTRAINT "ck_desktop_layouts_presentation_state" CHECK ((presentation_mode IN ('Freeform', 'Tiled', 'PhoneEmpty') AND primary_window_id IS NULL AND secondary_window_id IS NULL AND split_ratio_permille IS NULL) OR (presentation_mode = 'PhoneSingle' AND primary_window_id IS NOT NULL AND secondary_window_id IS NULL AND split_ratio_permille IS NULL) OR (presentation_mode = 'PhoneSplit' AND primary_window_id IS NOT NULL AND secondary_window_id IS NOT NULL AND primary_window_id <> secondary_window_id AND split_ratio_permille BETWEEN 250 AND 750)),
    CONSTRAINT "ck_desktop_layouts_revision" CHECK (revision >= 1),
    CONSTRAINT "fk_desktop_layouts_device" FOREIGN KEY ("user_id", "client_device_id") REFERENCES "client_devices" ("owner_user_id", "id") ON DELETE CASCADE
);
INSERT INTO "desktop_layouts__julos_new" (
    "id",
    "user_id",
    "workspace_class",
    "client_device_id",
    "name",
    "presentation_mode",
    "primary_window_id",
    "secondary_window_id",
    "split_ratio_permille",
    "display_count",
    "revision",
    "updated_at_utc")
SELECT
    "id",
    "user_id",
    CASE "viewport_class"
        WHEN 'Desktop' THEN 'DesktopSingle'
        WHEN 'Mobile' THEN 'Phone'
        ELSE "viewport_class"
    END,
    NULL,
    "name",
    CASE WHEN "viewport_class" = 'Mobile' THEN 'PhoneEmpty' ELSE 'Freeform' END,
    NULL,
    NULL,
    NULL,
    1,
    "revision",
    "updated_at_utc"
FROM "desktop_layouts";
DROP TABLE "desktop_layouts";
ALTER TABLE "desktop_layouts__julos_new" RENAME TO "desktop_layouts";
CREATE UNIQUE INDEX "ux_desktop_layouts_device" ON "desktop_layouts" ("user_id", "client_device_id", "workspace_class") WHERE client_device_id IS NOT NULL;
CREATE UNIQUE INDEX "ux_desktop_layouts_shared" ON "desktop_layouts" ("user_id", "workspace_class") WHERE client_device_id IS NULL;

-- desktop_windows
CREATE TABLE "desktop_windows__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_desktop_windows" PRIMARY KEY,
    "desktop_layout_id" TEXT NOT NULL,
    "application_definition_id" TEXT NOT NULL,
    "launch_target_id" TEXT NULL,
    "state" TEXT NOT NULL,
    "x" INTEGER NOT NULL,
    "y" INTEGER NOT NULL,
    "width" INTEGER NOT NULL,
    "height" INTEGER NOT NULL,
    "restore_x" INTEGER NOT NULL,
    "restore_y" INTEGER NOT NULL,
    "restore_width" INTEGER NOT NULL,
    "restore_height" INTEGER NOT NULL,
    "z_index" INTEGER NOT NULL,
    "workspace_class" TEXT NOT NULL,
    "display_slot" INTEGER NOT NULL,
    "session_reference_id" TEXT NULL,
    "created_at_utc" TEXT NOT NULL,
    "updated_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_desktop_windows_bounds" CHECK (width BETWEEN 1 AND 16384 AND height BETWEEN 1 AND 16384 AND restore_width BETWEEN 1 AND 16384 AND restore_height BETWEEN 1 AND 16384 AND abs(x) <= 65536 AND abs(y) <= 65536 AND abs(restore_x) <= 65536 AND abs(restore_y) <= 65536),
    CONSTRAINT "ck_desktop_windows_display_slot" CHECK (display_slot >= 0 AND (workspace_class = 'DesktopMulti' OR display_slot = 0)),
    CONSTRAINT "ck_desktop_windows_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_desktop_windows_z_index" CHECK (z_index >= 0),
    CONSTRAINT "fk_desktop_windows_application" FOREIGN KEY ("application_definition_id") REFERENCES "application_definitions" ("id") ON DELETE RESTRICT,
    CONSTRAINT "fk_desktop_windows_launch_target" FOREIGN KEY ("launch_target_id") REFERENCES "launch_targets" ("id") ON DELETE SET NULL,
    CONSTRAINT "fk_desktop_windows_layout" FOREIGN KEY ("desktop_layout_id", "workspace_class") REFERENCES "desktop_layouts" ("id", "workspace_class") ON DELETE CASCADE,
    CONSTRAINT "fk_desktop_windows_session" FOREIGN KEY ("session_reference_id") REFERENCES "session_references" ("id") ON DELETE SET NULL
);
-- Each window's class is backfilled from the layout that holds it, which is what lets the
-- database refuse a cross-class write from here on. Every slot starts at zero because no
-- stored layout has ever had more than one display.
INSERT INTO "desktop_windows__julos_new" (
    "id",
    "desktop_layout_id",
    "application_definition_id",
    "launch_target_id",
    "state",
    "x",
    "y",
    "width",
    "height",
    "restore_x",
    "restore_y",
    "restore_width",
    "restore_height",
    "z_index",
    "workspace_class",
    "display_slot",
    "session_reference_id",
    "created_at_utc",
    "updated_at_utc",
    "revision")
SELECT
    w."id",
    w."desktop_layout_id",
    w."application_definition_id",
    w."launch_target_id",
    w."state",
    w."x",
    w."y",
    w."width",
    w."height",
    w."restore_x",
    w."restore_y",
    w."restore_width",
    w."restore_height",
    w."z_index",
    l."workspace_class",
    0,
    w."session_reference_id",
    w."created_at_utc",
    w."updated_at_utc",
    w."revision"
FROM "desktop_windows" AS w
JOIN "desktop_layouts" AS l ON l."id" = w."desktop_layout_id";
DROP TABLE "desktop_windows";
ALTER TABLE "desktop_windows__julos_new" RENAME TO "desktop_windows";
CREATE INDEX "IX_desktop_windows_application_definition_id" ON "desktop_windows" ("application_definition_id");
CREATE INDEX "IX_desktop_windows_desktop_layout_id_workspace_class" ON "desktop_windows" ("desktop_layout_id", "workspace_class");
CREATE INDEX "IX_desktop_windows_launch_target_id" ON "desktop_windows" ("launch_target_id");
CREATE INDEX "IX_desktop_windows_session_reference_id" ON "desktop_windows" ("session_reference_id");
CREATE UNIQUE INDEX "ux_desktop_windows_layout_z_index" ON "desktop_windows" ("desktop_layout_id", "z_index");

-- A migrated phone keeps every window. The foreground one is the non-minimized window
-- nearest the front, ties broken by identity, so migrating the same database twice
-- produces the same result. Split is never produced: showing two windows at once is an
-- explicit user action and is not inferred from a layout that merely held several windows.
UPDATE "desktop_layouts"
SET "presentation_mode" = 'PhoneSingle',
    "primary_window_id" = (
        SELECT w."id"
        FROM "desktop_windows" AS w
        WHERE w."desktop_layout_id" = "desktop_layouts"."id" AND w."state" <> 'Minimized'
        ORDER BY w."z_index" DESC, w."id"
        LIMIT 1)
WHERE "workspace_class" = 'Phone'
  AND EXISTS (
        SELECT 1
        FROM "desktop_windows" AS w
        WHERE w."desktop_layout_id" = "desktop_layouts"."id" AND w."state" <> 'Minimized');
