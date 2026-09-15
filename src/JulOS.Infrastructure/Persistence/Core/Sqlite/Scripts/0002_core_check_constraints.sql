-- 0002_core_check_constraints
-- Give SQLite the same CHECK constraints PostgreSQL enforces (docs/TECHNICAL_SPECIFICATION.md).
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model
-- changes and add a new migration instead of editing an applied one.

-- agent_capabilities
CREATE TABLE "agent_capabilities__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agent_capabilities" PRIMARY KEY,
    "agent_id" TEXT NOT NULL,
    "capability_name" TEXT NOT NULL,
    "capability_version" INTEGER NOT NULL,
    "enabled" INTEGER NOT NULL,
    "metadata_version" INTEGER NOT NULL,
    "metadata" TEXT NOT NULL,
    "observed_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_agent_capabilities_metadata_length" CHECK (length(metadata) <= 8192),
    CONSTRAINT "ck_agent_capabilities_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_agent_capabilities_version" CHECK (capability_version >= 1 AND metadata_version >= 1),
    CONSTRAINT "fk_agent_capabilities_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);
INSERT INTO "agent_capabilities__julos_new" SELECT * FROM "agent_capabilities";
DROP TABLE "agent_capabilities";
ALTER TABLE "agent_capabilities__julos_new" RENAME TO "agent_capabilities";
CREATE UNIQUE INDEX "ux_agent_capabilities_agent_name" ON "agent_capabilities" ("agent_id", "capability_name");

-- agent_commands
CREATE TABLE "agent_commands__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agent_commands" PRIMARY KEY,
    "agent_id" TEXT NOT NULL,
    "operation_key" TEXT NOT NULL,
    "command_type" TEXT NOT NULL,
    "payload_json" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "expires_at_utc" TEXT NOT NULL,
    "started_at_utc" TEXT NULL,
    "completed_at_utc" TEXT NULL,
    "result_json" TEXT NULL,
    "error_code" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_agent_commands_expiry" CHECK (expires_at_utc > created_at_utc),
    CONSTRAINT "ck_agent_commands_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_agent_commands_state" CHECK (state IN ('queued', 'running', 'succeeded', 'failed', 'expired', 'cancelled')),
    CONSTRAINT "fk_agent_commands_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);
INSERT INTO "agent_commands__julos_new" SELECT * FROM "agent_commands";
DROP TABLE "agent_commands";
ALTER TABLE "agent_commands__julos_new" RENAME TO "agent_commands";
CREATE INDEX "ix_agent_commands_agent_state_created" ON "agent_commands" ("agent_id", "state", "created_at_utc");
CREATE UNIQUE INDEX "ux_agent_commands_agent_operation_key" ON "agent_commands" ("agent_id", "operation_key");

-- agent_credentials
CREATE TABLE "agent_credentials__julos_new" (
    "agent_id" TEXT NOT NULL CONSTRAINT "pk_agent_credentials" PRIMARY KEY,
    "credential_hash" BLOB NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "rotated_at_utc" TEXT NULL,
    "revoked_at_utc" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_agent_credentials_revision" CHECK (revision >= 1),
    CONSTRAINT "fk_agent_credentials_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);
INSERT INTO "agent_credentials__julos_new" SELECT * FROM "agent_credentials";
DROP TABLE "agent_credentials";
ALTER TABLE "agent_credentials__julos_new" RENAME TO "agent_credentials";

-- agent_enrollment_tokens
CREATE TABLE "agent_enrollment_tokens__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agent_enrollment_tokens" PRIMARY KEY,
    "token_hash" BLOB NOT NULL,
    "created_by_user_id" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "expires_at_utc" TEXT NOT NULL,
    "redeemed_at_utc" TEXT NULL,
    "redeemed_by_agent_id" TEXT NULL,
    CONSTRAINT "ck_agent_enrollment_tokens_expiry" CHECK (expires_at_utc > created_at_utc),
    CONSTRAINT "ck_agent_enrollment_tokens_redemption" CHECK ((redeemed_at_utc IS NULL AND redeemed_by_agent_id IS NULL) OR (redeemed_at_utc IS NOT NULL AND redeemed_by_agent_id IS NOT NULL)),
    CONSTRAINT "fk_agent_enrollment_tokens_agent" FOREIGN KEY ("redeemed_by_agent_id") REFERENCES "agents" ("id") ON DELETE RESTRICT
);
INSERT INTO "agent_enrollment_tokens__julos_new" SELECT * FROM "agent_enrollment_tokens";
DROP TABLE "agent_enrollment_tokens";
ALTER TABLE "agent_enrollment_tokens__julos_new" RENAME TO "agent_enrollment_tokens";
CREATE INDEX "IX_agent_enrollment_tokens_redeemed_by_agent_id" ON "agent_enrollment_tokens" ("redeemed_by_agent_id");
CREATE UNIQUE INDEX "ux_agent_enrollment_tokens_hash" ON "agent_enrollment_tokens" ("token_hash");

-- agents
CREATE TABLE "agents__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agents" PRIMARY KEY,
    "name" TEXT NOT NULL,
    "machine_identity" TEXT NOT NULL,
    "operating_system" TEXT NOT NULL,
    "architecture" TEXT NOT NULL,
    "version" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "enrolled_at_utc" TEXT NOT NULL,
    "last_seen_at_utc" TEXT NULL,
    "revoked_at_utc" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_agents_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_agents_revoked_at" CHECK ((state = 'Revoked' AND revoked_at_utc IS NOT NULL) OR (state <> 'Revoked' AND revoked_at_utc IS NULL))
);
INSERT INTO "agents__julos_new" SELECT * FROM "agents";
DROP TABLE "agents";
ALTER TABLE "agents__julos_new" RENAME TO "agents";
CREATE INDEX "ix_agents_machine_identity" ON "agents" ("machine_identity");

-- application_definitions
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
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_application_definitions_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_application_definitions_window_size" CHECK (minimum_width BETWEEN 120 AND 16384 AND minimum_height BETWEEN 120 AND 16384 AND default_width BETWEEN minimum_width AND 16384 AND default_height BETWEEN minimum_height AND 16384)
);
INSERT INTO "application_definitions__julos_new" SELECT * FROM "application_definitions";
DROP TABLE "application_definitions";
ALTER TABLE "application_definitions__julos_new" RENAME TO "application_definitions";
CREATE UNIQUE INDEX "ux_application_definitions_package_stable_key" ON "application_definitions" ("owning_package_id", "stable_key");

-- authentication_setup
CREATE TABLE "authentication_setup__julos_new" (
    "id" INTEGER NOT NULL CONSTRAINT "pk_authentication_setup" PRIMARY KEY,
    "administrator_user_id" TEXT NULL,
    "completed_at_utc" TEXT NULL,
    CONSTRAINT "ck_authentication_setup_completion" CHECK ((completed_at_utc IS NULL AND administrator_user_id IS NULL) OR (completed_at_utc IS NOT NULL AND administrator_user_id IS NOT NULL)),
    CONSTRAINT "ck_authentication_setup_singleton" CHECK (id = 1),
    CONSTRAINT "fk_authentication_setup_administrator" FOREIGN KEY ("administrator_user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
);
INSERT INTO "authentication_setup__julos_new" SELECT * FROM "authentication_setup";
DROP TABLE "authentication_setup";
ALTER TABLE "authentication_setup__julos_new" RENAME TO "authentication_setup";
CREATE INDEX "IX_authentication_setup_administrator_user_id" ON "authentication_setup" ("administrator_user_id");

-- desktop_layouts
CREATE TABLE "desktop_layouts__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_desktop_layouts" PRIMARY KEY,
    "user_id" TEXT NOT NULL,
    "viewport_class" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "is_default" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    "updated_at_utc" TEXT NOT NULL,
    CONSTRAINT "ck_desktop_layouts_revision" CHECK (revision >= 1)
);
INSERT INTO "desktop_layouts__julos_new" SELECT * FROM "desktop_layouts";
DROP TABLE "desktop_layouts";
ALTER TABLE "desktop_layouts__julos_new" RENAME TO "desktop_layouts";
CREATE UNIQUE INDEX "ux_desktop_layouts_default_per_viewport" ON "desktop_layouts" ("user_id", "viewport_class") WHERE is_default;
CREATE UNIQUE INDEX "ux_desktop_layouts_user_viewport_name" ON "desktop_layouts" ("user_id", "viewport_class", "name");

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
    "session_reference_id" TEXT NULL,
    "created_at_utc" TEXT NOT NULL,
    "updated_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_desktop_windows_bounds" CHECK (width BETWEEN 1 AND 16384 AND height BETWEEN 1 AND 16384 AND restore_width BETWEEN 1 AND 16384 AND restore_height BETWEEN 1 AND 16384 AND abs(x) <= 65536 AND abs(y) <= 65536 AND abs(restore_x) <= 65536 AND abs(restore_y) <= 65536),
    CONSTRAINT "ck_desktop_windows_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_desktop_windows_z_index" CHECK (z_index >= 0),
    CONSTRAINT "fk_desktop_windows_application" FOREIGN KEY ("application_definition_id") REFERENCES "application_definitions" ("id") ON DELETE RESTRICT,
    CONSTRAINT "fk_desktop_windows_launch_target" FOREIGN KEY ("launch_target_id") REFERENCES "launch_targets" ("id") ON DELETE SET NULL,
    CONSTRAINT "fk_desktop_windows_layout" FOREIGN KEY ("desktop_layout_id") REFERENCES "desktop_layouts" ("id") ON DELETE CASCADE,
    CONSTRAINT "fk_desktop_windows_session" FOREIGN KEY ("session_reference_id") REFERENCES "session_references" ("id") ON DELETE SET NULL
);
INSERT INTO "desktop_windows__julos_new" SELECT * FROM "desktop_windows";
DROP TABLE "desktop_windows";
ALTER TABLE "desktop_windows__julos_new" RENAME TO "desktop_windows";
CREATE INDEX "IX_desktop_windows_application_definition_id" ON "desktop_windows" ("application_definition_id");
CREATE INDEX "IX_desktop_windows_launch_target_id" ON "desktop_windows" ("launch_target_id");
CREATE INDEX "IX_desktop_windows_session_reference_id" ON "desktop_windows" ("session_reference_id");
CREATE UNIQUE INDEX "ux_desktop_windows_layout_z_index" ON "desktop_windows" ("desktop_layout_id", "z_index");

-- launch_targets
CREATE TABLE "launch_targets__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_launch_targets" PRIMARY KEY,
    "application_definition_id" TEXT NOT NULL,
    "owning_package_id" TEXT NOT NULL,
    "external_identity" TEXT NOT NULL,
    "display_name" TEXT NOT NULL,
    "approval_state" TEXT NOT NULL,
    "first_observed_at_utc" TEXT NOT NULL,
    "last_observed_at_utc" TEXT NOT NULL,
    "approved_at_utc" TEXT NULL,
    "approved_by_user_id" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_launch_targets_approval" CHECK ((approval_state = 'Approved' AND approved_at_utc IS NOT NULL AND approved_by_user_id IS NOT NULL) OR (approval_state <> 'Approved' AND approved_at_utc IS NULL AND approved_by_user_id IS NULL)),
    CONSTRAINT "ck_launch_targets_revision" CHECK (revision >= 1),
    CONSTRAINT "fk_launch_targets_application" FOREIGN KEY ("application_definition_id") REFERENCES "application_definitions" ("id") ON DELETE CASCADE
);
INSERT INTO "launch_targets__julos_new" SELECT * FROM "launch_targets";
DROP TABLE "launch_targets";
ALTER TABLE "launch_targets__julos_new" RENAME TO "launch_targets";
CREATE INDEX "IX_launch_targets_application_definition_id" ON "launch_targets" ("application_definition_id");
CREATE UNIQUE INDEX "ux_launch_targets_package_external_identity" ON "launch_targets" ("owning_package_id", "external_identity");

-- operation_progress_events
CREATE TABLE "operation_progress_events__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_operation_progress_events" PRIMARY KEY,
    "operation_id" TEXT NOT NULL,
    "progress_percent" INTEGER NULL,
    "current_step" TEXT NOT NULL,
    "occurred_at_utc" TEXT NOT NULL,
    CONSTRAINT "ck_operation_progress_events_progress" CHECK (progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100),
    CONSTRAINT "fk_operation_progress_events_operation" FOREIGN KEY ("operation_id") REFERENCES "operations" ("id") ON DELETE CASCADE
);
INSERT INTO "operation_progress_events__julos_new" SELECT * FROM "operation_progress_events";
DROP TABLE "operation_progress_events";
ALTER TABLE "operation_progress_events__julos_new" RENAME TO "operation_progress_events";
CREATE INDEX "ix_operation_progress_events_order" ON "operation_progress_events" ("operation_id", "occurred_at_utc", "id");

-- operations
CREATE TABLE "operations__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_operations" PRIMARY KEY,
    "owner_user_id" TEXT NOT NULL,
    "operation_type" TEXT NOT NULL,
    "source_package_id" TEXT NULL,
    "target_reference" TEXT NOT NULL,
    "idempotency_key" TEXT NOT NULL,
    "request_fingerprint" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "progress_percent" INTEGER NULL,
    "current_step" TEXT NULL,
    "created_at_utc" TEXT NOT NULL,
    "started_at_utc" TEXT NULL,
    "completed_at_utc" TEXT NULL,
    "failure_code" TEXT NULL,
    "failure_detail" TEXT NULL,
    "correlation_id" TEXT NOT NULL,
    "cancellation_requested_at_utc" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_operations_lifecycle" CHECK ((state = 'Queued' AND started_at_utc IS NULL AND completed_at_utc IS NULL AND failure_code IS NULL AND failure_detail IS NULL) OR (state = 'Running' AND started_at_utc IS NOT NULL AND completed_at_utc IS NULL AND failure_code IS NULL AND failure_detail IS NULL) OR (state = 'Succeeded' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NULL AND failure_detail IS NULL) OR (state = 'Failed' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NOT NULL AND failure_detail IS NOT NULL) OR (state = 'Cancelled' AND completed_at_utc IS NOT NULL AND failure_code IS NULL AND failure_detail IS NULL)),
    CONSTRAINT "ck_operations_progress" CHECK (progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100),
    CONSTRAINT "ck_operations_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_operations_state" CHECK (state IN ('Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled')),
    CONSTRAINT "fk_operations_owner_user" FOREIGN KEY ("owner_user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
);
INSERT INTO "operations__julos_new" SELECT * FROM "operations";
DROP TABLE "operations";
ALTER TABLE "operations__julos_new" RENAME TO "operations";
CREATE INDEX "ix_operations_owner_created_at_utc" ON "operations" ("owner_user_id", "created_at_utc");
CREATE UNIQUE INDEX "ux_operations_owner_idempotency" ON "operations" ("owner_user_id", "idempotency_key");

-- package_installations
CREATE TABLE "package_installations__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_package_installations" PRIMARY KEY,
    "package_id" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    "fault_code" TEXT NULL,
    "fault_detail" TEXT NULL,
    "faulted_at_utc" TEXT NULL,
    CONSTRAINT "ck_package_installations_fault_metadata" CHECK ((state = 'Faulted' AND fault_code IS NOT NULL AND fault_detail IS NOT NULL AND faulted_at_utc IS NOT NULL) OR (state <> 'Faulted' AND fault_code IS NULL AND fault_detail IS NULL AND faulted_at_utc IS NULL)),
    CONSTRAINT "ck_package_installations_revision" CHECK (revision >= 1)
);
INSERT INTO "package_installations__julos_new" SELECT * FROM "package_installations";
DROP TABLE "package_installations";
ALTER TABLE "package_installations__julos_new" RENAME TO "package_installations";

-- permission_assignments
CREATE TABLE "permission_assignments__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_permission_assignments" PRIMARY KEY,
    "subject_kind" TEXT NOT NULL,
    "subject_id" TEXT NOT NULL,
    "permission" TEXT NOT NULL,
    "scope_kind" TEXT NOT NULL,
    "scope_id" TEXT NULL,
    "granted_at_utc" TEXT NOT NULL,
    "granted_by_user_id" TEXT NOT NULL,
    CONSTRAINT "ck_permission_assignments_scope" CHECK ((scope_kind = 'Global' AND scope_id IS NULL) OR (scope_kind <> 'Global' AND scope_id IS NOT NULL))
);
INSERT INTO "permission_assignments__julos_new" SELECT * FROM "permission_assignments";
DROP TABLE "permission_assignments";
ALTER TABLE "permission_assignments__julos_new" RENAME TO "permission_assignments";
CREATE UNIQUE INDEX "ux_permission_assignments_grant" ON "permission_assignments" ("subject_kind", "subject_id", "permission", "scope_kind", "scope_id");

-- problems
CREATE TABLE "problems__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_problems" PRIMARY KEY,
    "source_package_id" TEXT NOT NULL,
    "problem_type" TEXT NOT NULL,
    "stable_resource_identity" TEXT NOT NULL,
    "severity" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "title_key" TEXT NOT NULL,
    "first_detected_at_utc" TEXT NOT NULL,
    "last_observed_at_utc" TEXT NOT NULL,
    "acknowledged_at_utc" TEXT NULL,
    "acknowledged_by_user_id" TEXT NULL,
    "resolved_at_utc" TEXT NULL,
    "observation_count" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_problems_observation_count" CHECK (observation_count >= 1),
    CONSTRAINT "ck_problems_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_problems_state_timestamps" CHECK ((state = 'Active' AND acknowledged_at_utc IS NULL AND acknowledged_by_user_id IS NULL AND resolved_at_utc IS NULL) OR (state = 'Acknowledged' AND acknowledged_at_utc IS NOT NULL AND acknowledged_by_user_id IS NOT NULL AND resolved_at_utc IS NULL) OR (state = 'Resolved' AND resolved_at_utc IS NOT NULL) OR state = 'Suppressed')
);
INSERT INTO "problems__julos_new" SELECT * FROM "problems";
DROP TABLE "problems";
ALTER TABLE "problems__julos_new" RENAME TO "problems";
CREATE UNIQUE INDEX "ux_problems_identity" ON "problems" ("source_package_id", "problem_type", "stable_resource_identity");

-- remote_sessions
CREATE TABLE "remote_sessions__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_remote_sessions" PRIMARY KEY,
    "owner_user_id" TEXT NOT NULL,
    "caller_package_id" TEXT NOT NULL,
    "operation_key" TEXT NOT NULL,
    "request_identity" TEXT NOT NULL,
    "protocol" TEXT NOT NULL,
    "target_host" TEXT NOT NULL,
    "target_port" INTEGER NOT NULL,
    "secret_reference_id" TEXT NOT NULL,
    "profile_id" TEXT NULL,
    "network_profile_id" TEXT NULL,
    "viewport_width" INTEGER NOT NULL,
    "viewport_height" INTEGER NOT NULL,
    "device_scale_factor" TEXT NOT NULL,
    "idle_timeout_seconds" INTEGER NOT NULL,
    "maximum_session_seconds" INTEGER NOT NULL,
    "state" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "updated_at_utc" TEXT NOT NULL,
    "last_activity_at_utc" TEXT NOT NULL,
    "expires_at_utc" TEXT NOT NULL,
    "connected_at_utc" TEXT NULL,
    "ended_at_utc" TEXT NULL,
    "runtime_id" TEXT NULL,
    "display_kind" TEXT NULL,
    "display_contract_version" TEXT NULL,
    "display_endpoint" TEXT NULL,
    "display_expires_at_utc" TEXT NULL,
    "failure_code" TEXT NULL,
    "failure_detail" TEXT NULL,
    "failure_retryable" INTEGER NULL,
    "cancellation_operation_key" TEXT NULL,
    "cancellation_reason" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_remote_sessions_cancellation" CHECK ((state = 'cancelled' AND cancellation_operation_key IS NOT NULL) OR (state <> 'cancelled' AND cancellation_operation_key IS NULL AND cancellation_reason IS NULL)),
    CONSTRAINT "ck_remote_sessions_display" CHECK ((display_kind IS NULL AND display_contract_version IS NULL AND display_endpoint IS NULL AND display_expires_at_utc IS NULL) OR (state = 'connected' AND display_kind IS NOT NULL AND display_contract_version IS NOT NULL AND display_endpoint IS NOT NULL AND display_expires_at_utc IS NOT NULL)),
    CONSTRAINT "ck_remote_sessions_failure" CHECK ((state = 'failed' AND failure_code IS NOT NULL AND failure_detail IS NOT NULL AND failure_retryable IS NOT NULL) OR (state <> 'failed' AND failure_code IS NULL AND failure_detail IS NULL AND failure_retryable IS NULL)),
    CONSTRAINT "ck_remote_sessions_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_remote_sessions_state" CHECK (state IN ('requested', 'provisioning', 'connecting', 'connected', 'disconnecting', 'disconnected', 'cancelled', 'expired', 'failed')),
    CONSTRAINT "ck_remote_sessions_target_port" CHECK (target_port BETWEEN 1 AND 65535),
    CONSTRAINT "ck_remote_sessions_terminal_time" CHECK ((state IN ('disconnected', 'cancelled', 'expired', 'failed') AND ended_at_utc IS NOT NULL) OR (state NOT IN ('disconnected', 'cancelled', 'expired', 'failed') AND ended_at_utc IS NULL)),
    CONSTRAINT "ck_remote_sessions_timeouts" CHECK (idle_timeout_seconds BETWEEN 60 AND 86400 AND maximum_session_seconds BETWEEN 300 AND 604800 AND idle_timeout_seconds <= maximum_session_seconds),
    CONSTRAINT "ck_remote_sessions_timestamps" CHECK (updated_at_utc >= created_at_utc AND last_activity_at_utc >= created_at_utc AND expires_at_utc > created_at_utc),
    CONSTRAINT "ck_remote_sessions_viewport" CHECK (viewport_width BETWEEN 320 AND 7680 AND viewport_height BETWEEN 240 AND 4320 AND device_scale_factor BETWEEN 0.5 AND 4),
    CONSTRAINT "fk_remote_sessions_owner" FOREIGN KEY ("owner_user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
);
INSERT INTO "remote_sessions__julos_new" SELECT * FROM "remote_sessions";
DROP TABLE "remote_sessions";
ALTER TABLE "remote_sessions__julos_new" RENAME TO "remote_sessions";
CREATE INDEX "ix_remote_sessions_owner_package_page" ON "remote_sessions" ("owner_user_id", "caller_package_id", "id");
CREATE UNIQUE INDEX "ux_remote_sessions_owner_package_operation" ON "remote_sessions" ("owner_user_id", "caller_package_id", "operation_key");
CREATE UNIQUE INDEX "ux_remote_sessions_runtime" ON "remote_sessions" ("runtime_id") WHERE runtime_id IS NOT NULL;

-- roles
CREATE TABLE "roles__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "PK_roles" PRIMARY KEY,
    "description" TEXT NOT NULL,
    "is_system_role" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    "name" TEXT NULL,
    "normalized_name" TEXT NULL,
    "concurrency_stamp" TEXT NULL,
    CONSTRAINT "ck_roles_revision" CHECK (revision >= 1)
);
INSERT INTO "roles__julos_new" SELECT * FROM "roles";
DROP TABLE "roles";
ALTER TABLE "roles__julos_new" RENAME TO "roles";
CREATE UNIQUE INDEX "ux_roles_normalized_name" ON "roles" ("normalized_name");

-- secret_references
CREATE TABLE "secret_references__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_secret_references" PRIMARY KEY,
    "owning_scope_type" TEXT NOT NULL,
    "owning_scope_id" TEXT NULL,
    "purpose" TEXT NOT NULL,
    "storage_provider" TEXT NOT NULL,
    "encryption_key_id" TEXT NULL,
    "nonce" BLOB NULL,
    "ciphertext" BLOB NULL,
    "authentication_tag" BLOB NULL,
    "created_at_utc" TEXT NOT NULL,
    "rotated_at_utc" TEXT NULL,
    "deleted_at_utc" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_secret_references_deletion" CHECK (deleted_at_utc IS NULL OR deleted_at_utc >= created_at_utc),
    CONSTRAINT "ck_secret_references_protected_value" CHECK ((deleted_at_utc IS NULL AND encryption_key_id IS NOT NULL AND nonce IS NOT NULL AND octet_length(nonce) = 12 AND ciphertext IS NOT NULL AND octet_length(ciphertext) > 0 AND authentication_tag IS NOT NULL AND octet_length(authentication_tag) = 16) OR (deleted_at_utc IS NOT NULL AND encryption_key_id IS NULL AND nonce IS NULL AND ciphertext IS NULL AND authentication_tag IS NULL)),
    CONSTRAINT "ck_secret_references_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_secret_references_rotation" CHECK (rotated_at_utc IS NULL OR rotated_at_utc >= created_at_utc),
    CONSTRAINT "ck_secret_references_scope" CHECK ((owning_scope_type = 'System' AND owning_scope_id IS NULL) OR (owning_scope_type = 'Package' AND owning_scope_id IS NOT NULL))
);
INSERT INTO "secret_references__julos_new" SELECT * FROM "secret_references";
DROP TABLE "secret_references";
ALTER TABLE "secret_references__julos_new" RENAME TO "secret_references";
CREATE INDEX "ix_secret_references_scope_purpose" ON "secret_references" ("owning_scope_type", "owning_scope_id", "purpose");

-- session_references
CREATE TABLE "session_references__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_session_references" PRIMARY KEY,
    "owning_package_id" TEXT NOT NULL,
    "session_kind" TEXT NOT NULL,
    "target_reference" TEXT NOT NULL,
    "user_id" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "lifecycle_policy" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "connected_at_utc" TEXT NULL,
    "last_activity_at_utc" TEXT NOT NULL,
    "expires_at_utc" TEXT NULL,
    "ended_at_utc" TEXT NULL,
    "failure_code" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_session_references_ended_at" CHECK ((state = 'Ended' AND ended_at_utc IS NOT NULL) OR (state <> 'Ended' AND ended_at_utc IS NULL)),
    CONSTRAINT "ck_session_references_expiry" CHECK (expires_at_utc IS NULL OR expires_at_utc > created_at_utc),
    CONSTRAINT "ck_session_references_revision" CHECK (revision >= 1)
);
INSERT INTO "session_references__julos_new" SELECT * FROM "session_references";
DROP TABLE "session_references";
ALTER TABLE "session_references__julos_new" RENAME TO "session_references";

-- users
CREATE TABLE "users__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "PK_users" PRIMARY KEY,
    "display_name" TEXT NOT NULL,
    "preferred_language" TEXT NOT NULL,
    "time_zone" TEXT NOT NULL,
    "theme" TEXT NOT NULL,
    "motion" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "updated_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    "user_name" TEXT NULL,
    "normalized_user_name" TEXT NULL,
    "email" TEXT NULL,
    "normalized_email" TEXT NULL,
    "email_confirmed" INTEGER NOT NULL,
    "password_hash" TEXT NULL,
    "security_stamp" TEXT NULL,
    "concurrency_stamp" TEXT NULL,
    "phone_number" TEXT NULL,
    "phone_number_confirmed" INTEGER NOT NULL,
    "two_factor_enabled" INTEGER NOT NULL,
    "lockout_end_utc" TEXT NULL,
    "lockout_enabled" INTEGER NOT NULL,
    "access_failed_count" INTEGER NOT NULL,
    CONSTRAINT "ck_users_language" CHECK (preferred_language IN ('en', 'de')),
    CONSTRAINT "ck_users_motion" CHECK (motion IN ('enabled', 'reduced')),
    CONSTRAINT "ck_users_revision" CHECK (revision >= 1),
    CONSTRAINT "ck_users_theme" CHECK (theme IN ('system', 'light', 'dark'))
);
INSERT INTO "users__julos_new" SELECT * FROM "users";
DROP TABLE "users";
ALTER TABLE "users__julos_new" RENAME TO "users";
CREATE INDEX "ix_users_normalized_email" ON "users" ("normalized_email");
CREATE UNIQUE INDEX "ux_users_normalized_user_name" ON "users" ("normalized_user_name");

-- widget_placements
CREATE TABLE "widget_placements__julos_new" (
    "id" TEXT NOT NULL CONSTRAINT "pk_widget_placements" PRIMARY KEY,
    "desktop_layout_id" TEXT NOT NULL,
    "widget_key" TEXT NOT NULL,
    "grid_column" INTEGER NOT NULL,
    "grid_row" INTEGER NOT NULL,
    "width_units" INTEGER NOT NULL,
    "height_units" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "ck_widget_placements_grid" CHECK (grid_column >= 0 AND grid_row >= 0 AND width_units > 0 AND height_units > 0 AND grid_column + width_units <= 64 AND grid_row + height_units <= 64),
    CONSTRAINT "ck_widget_placements_revision" CHECK (revision >= 1),
    CONSTRAINT "fk_widget_placements_layout" FOREIGN KEY ("desktop_layout_id") REFERENCES "desktop_layouts" ("id") ON DELETE CASCADE
);;
INSERT INTO "widget_placements__julos_new" SELECT * FROM "widget_placements";
DROP TABLE "widget_placements";
ALTER TABLE "widget_placements__julos_new" RENAME TO "widget_placements";
CREATE INDEX "IX_widget_placements_desktop_layout_id" ON "widget_placements" ("desktop_layout_id");
