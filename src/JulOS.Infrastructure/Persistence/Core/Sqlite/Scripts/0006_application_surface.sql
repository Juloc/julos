-- 0006_application_surface
-- Persist the Surface lifecycle contract a package declares for an application
-- (MOB-006, docs/MOBILE_PWA.md sections 10 and 11).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model changes
-- and add a new migration instead of editing an applied one.
--
-- The declaration is what lets Server refuse a background mode the application never said
-- it supports, so a stored preference and the resolved Surface state cannot disagree.
--
-- The table is rebuilt rather than altered because the new columns sit before `revision`
-- in the model, and SQLite would otherwise append them; the drift guard compares the
-- migrated schema with the Entity Framework model exactly.

CREATE TABLE "application_definitions__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_application_definitions" PRIMARY KEY,
    "owning_package_id" TEXT NOT NULL,
    "stable_key" TEXT NOT NULL,
    "display_name_key" TEXT NOT NULL,
    "instance_policy" TEXT NOT NULL,
    "default_width" INTEGER NOT NULL,
    "default_height" INTEGER NOT NULL,
    "minimum_width" INTEGER NOT NULL,
    "minimum_height" INTEGER NOT NULL,
    "is_enabled" INTEGER NOT NULL,
    "surface_contract_version" TEXT NULL,
    "surface_supports_keep_active" INTEGER NOT NULL,
    "surface_handles_back" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_application_definitions_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_application_definitions_window_size" CHECK (minimum_width BETWEEN 120 AND 16384 AND minimum_height BETWEEN 120 AND 16384 AND default_width BETWEEN minimum_width AND 16384 AND default_height BETWEEN minimum_height AND 16384)
);

-- An application registered before this migration declared no Surface contract. It is
-- recorded as declaring none rather than as declaring the default, so a package that never
-- said it can stay active cannot be given that mode until it re-registers and says so.
INSERT INTO "application_definitions__julos_new" (
    "id",
    "owning_package_id",
    "stable_key",
    "display_name_key",
    "instance_policy",
    "default_width",
    "default_height",
    "minimum_width",
    "minimum_height",
    "is_enabled",
    "surface_contract_version",
    "surface_supports_keep_active",
    "surface_handles_back",
    "revision")
SELECT
    "id",
    "owning_package_id",
    "stable_key",
    "display_name_key",
    "instance_policy",
    "default_width",
    "default_height",
    "minimum_width",
    "minimum_height",
    "is_enabled",
    NULL,
    0,
    0,
    "revision"
FROM "application_definitions";
DROP TABLE "application_definitions";
ALTER TABLE "application_definitions__julos_new" RENAME TO "application_definitions";
CREATE UNIQUE INDEX "ux_application_definitions_package_stable_key" ON "application_definitions" ("owning_package_id", "stable_key");
