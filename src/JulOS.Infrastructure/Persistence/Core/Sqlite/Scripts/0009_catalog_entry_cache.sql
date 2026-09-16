-- 0009_catalog_entry_cache
-- The cached catalog a refresh produces, and the identity a source claimed
-- (CAT-002, docs/APPLICATION_CATALOG.md sections 3, 5 and 6).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model changes
-- and add a new migration instead of editing an applied one.
--
-- `catalog_sources` is rebuilt rather than altered because `source_identity` sits before
-- `trust_level` in the model, and the drift guard compares the migrated schema with the
-- Entity Framework model exactly. The rebuild carries every existing row across; no source
-- has an identity yet, because none has completed a refresh under any released build.

CREATE TABLE "catalog_sources__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_catalog_sources" PRIMARY KEY,
    "kind" TEXT NOT NULL,
    "display_name" TEXT NOT NULL,
    "location" TEXT NOT NULL,
    "authentication_secret_reference_id" TEXT NULL,
    "source_identity" TEXT NULL,
    "trust_level" TEXT NOT NULL,
    "enabled" INTEGER NOT NULL,
    "deleted_at_utc" TEXT NULL,
    "last_successful_revision" INTEGER NULL,
    "last_successful_digest" TEXT NULL,
    "last_refresh_at_utc" TEXT NULL,
    "last_refresh_state" TEXT NOT NULL,
    "last_failure_code" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_catalog_sources_kind" CHECK (kind IN ('Official', 'Https', 'Git', 'Oci', 'Local')),
    CONSTRAINT "ck_catalog_sources_refresh_evidence" CHECK ((last_refresh_state = 'Never' AND last_successful_revision IS NULL) OR (last_refresh_state <> 'Never' AND last_successful_revision IS NOT NULL AND last_successful_digest IS NOT NULL)),
    CONSTRAINT "ck_catalog_sources_refresh_state" CHECK (last_refresh_state IN ('Never', 'Fresh', 'Stale')),
    CONSTRAINT "ck_catalog_sources_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_catalog_sources_trust_level" CHECK ((kind = 'Official' AND trust_level = 'Official') OR (kind <> 'Official' AND trust_level IN ('AdministratorTrusted', 'Custom')))
);

INSERT INTO "catalog_sources__julos_new" (
    "id",
    "kind",
    "display_name",
    "location",
    "authentication_secret_reference_id",
    "source_identity",
    "trust_level",
    "enabled",
    "deleted_at_utc",
    "last_successful_revision",
    "last_successful_digest",
    "last_refresh_at_utc",
    "last_refresh_state",
    "last_failure_code",
    "revision")
SELECT
    "id",
    "kind",
    "display_name",
    "location",
    "authentication_secret_reference_id",
    NULL,
    "trust_level",
    "enabled",
    "deleted_at_utc",
    "last_successful_revision",
    "last_successful_digest",
    "last_refresh_at_utc",
    "last_refresh_state",
    "last_failure_code",
    "revision"
FROM "catalog_sources";
DROP TABLE "catalog_sources";
ALTER TABLE "catalog_sources__julos_new" RENAME TO "catalog_sources";
CREATE UNIQUE INDEX "ux_catalog_sources_location" ON "catalog_sources" ("location") WHERE deleted_at_utc IS NULL;

-- The cached catalog itself. A refresh replaces every row of one source or none of them,
-- which is what keeps a failed refresh serving the last valid catalog rather than half of
-- two. `definition` holds the canonical bytes the definition digest was taken over, so the
-- cache can serve a source that has since become unreachable.
CREATE TABLE "catalog_entry_cache" (
    "id" TEXT NOT NULL CONSTRAINT "pk_catalog_entry_cache" PRIMARY KEY,
    "catalog_source_id" TEXT NOT NULL,
    "app_id" TEXT NOT NULL,
    "version" TEXT NOT NULL,
    "source_revision" INTEGER NOT NULL,
    "source_digest" TEXT NOT NULL,
    "definition_digest" TEXT NOT NULL,
    "definition" TEXT NOT NULL,
    "publisher_id" TEXT NULL,
    "signature_key_id" TEXT NULL,
    "public_key_fingerprint" TEXT NULL,
    "signature_state" TEXT NOT NULL,
    "trust_assessment_digest" TEXT NOT NULL,
    "cached_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_catalog_entry_cache_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_catalog_entry_cache_signature_state" CHECK (signature_state IN ('TrustedSigned', 'UnknownSigned', 'NotSigned', 'InvalidSignature')),
    CONSTRAINT "ck_catalog_entry_cache_signer" CHECK ((signature_state = 'NotSigned' AND publisher_id IS NULL AND signature_key_id IS NULL AND public_key_fingerprint IS NULL) OR (signature_state <> 'NotSigned' AND publisher_id IS NOT NULL AND signature_key_id IS NOT NULL AND public_key_fingerprint IS NOT NULL)),
    CONSTRAINT "fk_catalog_entry_cache_source" FOREIGN KEY ("catalog_source_id") REFERENCES "catalog_sources" ("id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "ux_catalog_entry_cache_identity" ON "catalog_entry_cache" ("catalog_source_id", "app_id", "version");
