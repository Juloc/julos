-- 0008_catalog_sources
-- Catalog sources and the publisher keys observed in them
-- (CAT-002, docs/APPLICATION_CATALOG.md).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model changes
-- and add a new migration instead of editing an applied one.
--
-- Purely additive: no existing table is touched, so an upgraded database keeps every row.
--
-- A source is a reference, not a container. Removing one writes `deleted_at_utc` instead of
-- deleting the row, which is why the uniqueness of `location` is partial: an installed
-- application keeps a resolvable reference to the source it came from, and the same
-- location may be added again afterwards.

CREATE TABLE "catalog_sources" (
    "id" TEXT NOT NULL CONSTRAINT "pk_catalog_sources" PRIMARY KEY,
    "kind" TEXT NOT NULL,
    "display_name" TEXT NOT NULL,
    "location" TEXT NOT NULL,
    "authentication_secret_reference_id" TEXT NULL,
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

-- Trust is stored as evidence: what the source published, when it was first and last seen,
-- and separately what an administrator decided about it. A later decision changes future
-- evaluation and never rewrites what an installed application was verified with.
CREATE TABLE "catalog_publisher_keys" (
    "id" TEXT NOT NULL CONSTRAINT "pk_catalog_publisher_keys" PRIMARY KEY,
    "catalog_source_id" TEXT NOT NULL,
    "publisher_id" TEXT NOT NULL,
    "key_id" TEXT NOT NULL,
    "algorithm" TEXT NOT NULL,
    "public_key_spki" TEXT NOT NULL,
    "public_key_fingerprint" TEXT NOT NULL,
    "valid_from_utc" TEXT NOT NULL,
    "valid_until_utc" TEXT NULL,
    "revoked_at_utc" TEXT NULL,
    "administrator_trust_state" TEXT NOT NULL,
    "administrator_decision_by_user_id" TEXT NULL,
    "administrator_decision_at_utc" TEXT NULL,
    "first_observed_source_revision" INTEGER NOT NULL,
    "last_observed_source_revision" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_catalog_publisher_keys_decision" CHECK ((administrator_trust_state = 'Unknown' AND administrator_decision_by_user_id IS NULL AND administrator_decision_at_utc IS NULL) OR (administrator_trust_state <> 'Unknown' AND administrator_decision_by_user_id IS NOT NULL AND administrator_decision_at_utc IS NOT NULL)),
    CONSTRAINT "ck_catalog_publisher_keys_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_catalog_publisher_keys_trust" CHECK (administrator_trust_state IN ('Unknown', 'Trusted', 'Distrusted')),
    CONSTRAINT "ck_catalog_publisher_keys_validity" CHECK (valid_until_utc IS NULL OR valid_until_utc > valid_from_utc),
    CONSTRAINT "fk_catalog_publisher_keys_source" FOREIGN KEY ("catalog_source_id") REFERENCES "catalog_sources" ("id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "ux_catalog_sources_location" ON "catalog_sources" ("location") WHERE deleted_at_utc IS NULL;

-- A key identity belongs to exactly one key within one source, because a source that
-- republishes an identity with different bytes is replacing what vouches for everything it
-- ever published.
CREATE UNIQUE INDEX "ux_catalog_publisher_keys_identity" ON "catalog_publisher_keys" ("catalog_source_id", "publisher_id", "key_id");
