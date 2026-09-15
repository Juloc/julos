-- 0004_client_devices
-- Client-device registration, per-device workspace preferences and application
-- background-execution preferences (MOB-003, docs/MOBILE_PWA.md sections 3, 4 and 11).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model changes
-- and add a new migration instead of editing an applied one.
--
-- Purely additive: no existing table is touched, so an upgraded database keeps every row.

CREATE TABLE "application_execution_preferences" (
    "id" TEXT NOT NULL CONSTRAINT "pk_application_execution_preferences" PRIMARY KEY,
    "owner_user_id" TEXT NOT NULL,
    "application_definition_id" TEXT NOT NULL,
    "workspace_class" TEXT NOT NULL,
    "client_device_id" TEXT NULL,
    "background_mode" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_application_execution_preferences_mode" CHECK (background_mode IN ('Suspend', 'KeepSurfaceActive')),
    CONSTRAINT "ck_application_execution_preferences_revision" CHECK (revision >= 1),
    CONSTRAINT "fk_application_execution_preferences_device" FOREIGN KEY ("client_device_id") REFERENCES "client_devices" ("id") ON DELETE CASCADE
);

CREATE TABLE "client_devices" (
    "id" TEXT NOT NULL CONSTRAINT "pk_client_devices" PRIMARY KEY,
    "owner_user_id" TEXT NOT NULL,
    "client_instance_key_hash" TEXT NOT NULL,
    "display_name" TEXT NOT NULL,
    "last_detected_workspace_class" TEXT NOT NULL,
    "workspace_class_override" TEXT NULL,
    "created_at_utc" TEXT NOT NULL,
    "last_seen_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_client_devices_override" CHECK (workspace_class_override IS NULL OR workspace_class_override IN ('Phone', 'Tablet', 'DesktopSingle')),
    CONSTRAINT "ck_client_devices_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_client_devices_seen" CHECK (last_seen_at_utc >= created_at_utc),
    CONSTRAINT "ck_client_devices_workspace" CHECK (last_detected_workspace_class IN ('Phone', 'Tablet', 'DesktopSingle'))
);

CREATE TABLE "device_workspace_preferences" (
    "client_device_id" TEXT NOT NULL,
    "workspace_class" TEXT NOT NULL,
    "layout_scope" TEXT NOT NULL,
    "restore_mode" TEXT NOT NULL,
    CONSTRAINT "pk_device_workspace_preferences" PRIMARY KEY ("client_device_id", "workspace_class"),
    CONSTRAINT "ck_device_workspace_preferences_restore" CHECK (restore_mode IN ('Resume', 'Fresh')),
    CONSTRAINT "ck_device_workspace_preferences_scope" CHECK (layout_scope IN ('Shared', 'Device')),
    CONSTRAINT "fk_device_workspace_preferences_device" FOREIGN KEY ("client_device_id") REFERENCES "client_devices" ("id") ON DELETE CASCADE
);

CREATE INDEX "IX_application_execution_preferences_client_device_id" ON "application_execution_preferences" ("client_device_id");
CREATE INDEX "ix_client_devices_owner" ON "client_devices" ("owner_user_id");
CREATE UNIQUE INDEX "ux_application_execution_preferences_device" ON "application_execution_preferences" ("owner_user_id", "application_definition_id", "workspace_class", "client_device_id") WHERE client_device_id IS NOT NULL;
CREATE UNIQUE INDEX "ux_application_execution_preferences_shared" ON "application_execution_preferences" ("owner_user_id", "application_definition_id", "workspace_class") WHERE client_device_id IS NULL;
CREATE UNIQUE INDEX "ux_client_devices_key_hash" ON "client_devices" ("client_instance_key_hash");
