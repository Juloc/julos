-- 0007_package_signature_state
-- Record how much is known about who produced an installed package
-- (PKG-013, docs/PACKAGES.md and docs/OFFICIAL_PACKAGE_RELEASES.md).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model changes
-- and add a new migration instead of editing an applied one.
--
-- This is trust, not integrity. Every installed artifact was already verified byte for byte
-- against its recorded digest; what this records is whether the signature covering those
-- bytes was made by a key this installation trusts. Code that is not trusted runs on the
-- isolated path.
--
-- The table is rebuilt rather than altered because the new column sits before `revision` in
-- the model, and the drift guard compares the migrated schema with the Entity Framework
-- model exactly.

CREATE TABLE "package_installations__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_package_installations" PRIMARY KEY,
    "package_id" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "signature_state" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    "fault_code" TEXT NULL,
    "fault_detail" TEXT NULL,
    "faulted_at_utc" TEXT NULL,
    CONSTRAINT "ck_package_installations_fault_metadata" CHECK ((state = 'Faulted' AND fault_code IS NOT NULL AND fault_detail IS NOT NULL AND faulted_at_utc IS NOT NULL) OR (state <> 'Faulted' AND fault_code IS NULL AND fault_detail IS NULL AND faulted_at_utc IS NULL)),
    CONSTRAINT "ck_package_installations_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_package_installations_signature_state" CHECK (signature_state IN ('TrustedSigned', 'UnknownSigned', 'NotSigned'))
);

-- Everything installed before this migration passed trusted-publisher verification: it was
-- the only way to install at all. Recording it as trusted therefore states what actually
-- happened rather than assuming a default.
INSERT INTO "package_installations__julos_new" (
    "id",
    "package_id",
    "state",
    "signature_state",
    "revision",
    "fault_code",
    "fault_detail",
    "faulted_at_utc")
SELECT
    "id",
    "package_id",
    "state",
    'TrustedSigned',
    "revision",
    "fault_code",
    "fault_detail",
    "faulted_at_utc"
FROM "package_installations";
DROP TABLE "package_installations";
ALTER TABLE "package_installations__julos_new" RENAME TO "package_installations";
