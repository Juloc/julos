-- 0001_baseline
-- Core schema as created by every JulOS release up to 0.4.0-beta.42.
--
-- Generated from the Core model. Do not edit by hand: regenerate when the model
-- changes and add a new migration instead of editing an applied one.

CREATE TABLE "agent_capabilities" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agent_capabilities" PRIMARY KEY,
    "agent_id" TEXT NOT NULL,
    "capability_name" TEXT NOT NULL,
    "capability_version" INTEGER NOT NULL,
    "enabled" INTEGER NOT NULL,
    "metadata_version" INTEGER NOT NULL,
    "metadata" TEXT NOT NULL,
    "observed_at_utc" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "fk_agent_capabilities_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);

CREATE TABLE "agent_commands" (
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
    CONSTRAINT "fk_agent_commands_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);

CREATE TABLE "agent_credentials" (
    "agent_id" TEXT NOT NULL CONSTRAINT "pk_agent_credentials" PRIMARY KEY,
    "credential_hash" BLOB NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "rotated_at_utc" TEXT NULL,
    "revoked_at_utc" TEXT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "fk_agent_credentials_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);

CREATE TABLE "agent_enrollment_tokens" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agent_enrollment_tokens" PRIMARY KEY,
    "token_hash" BLOB NOT NULL,
    "created_by_user_id" TEXT NOT NULL,
    "description" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "expires_at_utc" TEXT NOT NULL,
    "redeemed_at_utc" TEXT NULL,
    "redeemed_by_agent_id" TEXT NULL,
    CONSTRAINT "fk_agent_enrollment_tokens_agent" FOREIGN KEY ("redeemed_by_agent_id") REFERENCES "agents" ("id") ON DELETE RESTRICT
);

CREATE TABLE "agent_metric_samples" (
    "id" TEXT NOT NULL CONSTRAINT "pk_agent_metric_samples" PRIMARY KEY,
    "agent_id" TEXT NOT NULL,
    "metric_name" TEXT NOT NULL,
    "value" REAL NULL,
    "unit" TEXT NOT NULL,
    "labels_json" TEXT NOT NULL,
    "observed_at_utc" TEXT NOT NULL,
    "received_at_utc" TEXT NOT NULL,
    CONSTRAINT "fk_agent_metric_samples_agent" FOREIGN KEY ("agent_id") REFERENCES "agents" ("id") ON DELETE CASCADE
);

CREATE TABLE "agents" (
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
    "revision" INTEGER NOT NULL
);

CREATE TABLE "application_definitions" (
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
    "revision" INTEGER NOT NULL
);

CREATE TABLE "application_viewports" (
    "application_definition_id" TEXT NOT NULL,
    "viewport_class" TEXT NOT NULL,
    CONSTRAINT "pk_application_viewports" PRIMARY KEY ("application_definition_id", "viewport_class"),
    CONSTRAINT "fk_application_viewports_application" FOREIGN KEY ("application_definition_id") REFERENCES "application_definitions" ("id") ON DELETE CASCADE
);

CREATE TABLE "audit_events" (
    "id" TEXT NOT NULL CONSTRAINT "pk_audit_events" PRIMARY KEY,
    "occurred_at_utc" TEXT NOT NULL,
    "user_id" TEXT NULL,
    "agent_id" TEXT NULL,
    "source_package_id" TEXT NULL,
    "action" TEXT NOT NULL,
    "target_type" TEXT NOT NULL,
    "target_id" TEXT NOT NULL,
    "outcome" TEXT NOT NULL,
    "correlation_id" TEXT NOT NULL,
    "remote_address" TEXT NULL,
    "summary" TEXT NOT NULL,
    "safe_details" TEXT NOT NULL
);

CREATE TABLE "authentication_setup" (
    "id" INTEGER NOT NULL CONSTRAINT "pk_authentication_setup" PRIMARY KEY,
    "administrator_user_id" TEXT NULL,
    "completed_at_utc" TEXT NULL,
    CONSTRAINT "fk_authentication_setup_administrator" FOREIGN KEY ("administrator_user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
);

CREATE TABLE "desktop_layouts" (
    "id" TEXT NOT NULL CONSTRAINT "pk_desktop_layouts" PRIMARY KEY,
    "user_id" TEXT NOT NULL,
    "viewport_class" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "is_default" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    "updated_at_utc" TEXT NOT NULL
);

CREATE TABLE "desktop_windows" (
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
    CONSTRAINT "fk_desktop_windows_application" FOREIGN KEY ("application_definition_id") REFERENCES "application_definitions" ("id") ON DELETE RESTRICT,
    CONSTRAINT "fk_desktop_windows_launch_target" FOREIGN KEY ("launch_target_id") REFERENCES "launch_targets" ("id") ON DELETE SET NULL,
    CONSTRAINT "fk_desktop_windows_layout" FOREIGN KEY ("desktop_layout_id") REFERENCES "desktop_layouts" ("id") ON DELETE CASCADE,
    CONSTRAINT "fk_desktop_windows_session" FOREIGN KEY ("session_reference_id") REFERENCES "session_references" ("id") ON DELETE SET NULL
);

CREATE TABLE "launch_targets" (
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
    CONSTRAINT "fk_launch_targets_application" FOREIGN KEY ("application_definition_id") REFERENCES "application_definitions" ("id") ON DELETE CASCADE
);

CREATE TABLE "notifications" (
    "id" TEXT NOT NULL CONSTRAINT "pk_notifications" PRIMARY KEY,
    "user_id" TEXT NOT NULL,
    "source_package_id" TEXT NULL,
    "severity" TEXT NOT NULL,
    "title_key" TEXT NOT NULL,
    "deduplication_key" TEXT NOT NULL,
    "created_at_utc" TEXT NOT NULL,
    "read_at_utc" TEXT NULL,
    "action_link" TEXT NULL
);

CREATE TABLE "operation_progress_events" (
    "id" TEXT NOT NULL CONSTRAINT "pk_operation_progress_events" PRIMARY KEY,
    "operation_id" TEXT NOT NULL,
    "progress_percent" INTEGER NULL,
    "current_step" TEXT NOT NULL,
    "occurred_at_utc" TEXT NOT NULL,
    CONSTRAINT "fk_operation_progress_events_operation" FOREIGN KEY ("operation_id") REFERENCES "operations" ("id") ON DELETE CASCADE
);

CREATE TABLE "operations" (
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
    CONSTRAINT "fk_operations_owner_user" FOREIGN KEY ("owner_user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
);

CREATE TABLE "package_installations" (
    "id" TEXT NOT NULL CONSTRAINT "pk_package_installations" PRIMARY KEY,
    "package_id" TEXT NOT NULL,
    "state" TEXT NOT NULL,
    "revision" INTEGER NOT NULL,
    "fault_code" TEXT NULL,
    "fault_detail" TEXT NULL,
    "faulted_at_utc" TEXT NULL
);

CREATE TABLE "permission_assignments" (
    "id" TEXT NOT NULL CONSTRAINT "pk_permission_assignments" PRIMARY KEY,
    "subject_kind" TEXT NOT NULL,
    "subject_id" TEXT NOT NULL,
    "permission" TEXT NOT NULL,
    "scope_kind" TEXT NOT NULL,
    "scope_id" TEXT NULL,
    "granted_at_utc" TEXT NOT NULL,
    "granted_by_user_id" TEXT NOT NULL
);

CREATE TABLE "problems" (
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
    "revision" INTEGER NOT NULL
);

CREATE TABLE "remote_sessions" (
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
    CONSTRAINT "fk_remote_sessions_owner" FOREIGN KEY ("owner_user_id") REFERENCES "users" ("id") ON DELETE RESTRICT
);

CREATE TABLE "role_claims" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_role_claims" PRIMARY KEY AUTOINCREMENT,
    "role_id" TEXT NOT NULL,
    "claim_type" TEXT NULL,
    "claim_value" TEXT NULL,
    CONSTRAINT "FK_role_claims_roles_role_id" FOREIGN KEY ("role_id") REFERENCES "roles" ("id") ON DELETE CASCADE
);

CREATE TABLE "roles" (
    "id" TEXT NOT NULL CONSTRAINT "PK_roles" PRIMARY KEY,
    "description" TEXT NOT NULL,
    "is_system_role" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    "name" TEXT NULL,
    "normalized_name" TEXT NULL,
    "concurrency_stamp" TEXT NULL
);

CREATE TABLE "secret_references" (
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
    "revision" INTEGER NOT NULL
);

CREATE TABLE "session_references" (
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
    "revision" INTEGER NOT NULL
);

CREATE TABLE "user_claims" (
    "id" INTEGER NOT NULL CONSTRAINT "PK_user_claims" PRIMARY KEY AUTOINCREMENT,
    "user_id" TEXT NOT NULL,
    "claim_type" TEXT NULL,
    "claim_value" TEXT NULL,
    CONSTRAINT "FK_user_claims_users_user_id" FOREIGN KEY ("user_id") REFERENCES "users" ("id") ON DELETE CASCADE
);

CREATE TABLE "user_logins" (
    "login_provider" TEXT NOT NULL,
    "provider_key" TEXT NOT NULL,
    "provider_display_name" TEXT NULL,
    "user_id" TEXT NOT NULL,
    CONSTRAINT "PK_user_logins" PRIMARY KEY ("login_provider", "provider_key"),
    CONSTRAINT "FK_user_logins_users_user_id" FOREIGN KEY ("user_id") REFERENCES "users" ("id") ON DELETE CASCADE
);

CREATE TABLE "user_roles" (
    "user_id" TEXT NOT NULL,
    "role_id" TEXT NOT NULL,
    CONSTRAINT "PK_user_roles" PRIMARY KEY ("user_id", "role_id"),
    CONSTRAINT "FK_user_roles_roles_role_id" FOREIGN KEY ("role_id") REFERENCES "roles" ("id") ON DELETE CASCADE,
    CONSTRAINT "FK_user_roles_users_user_id" FOREIGN KEY ("user_id") REFERENCES "users" ("id") ON DELETE CASCADE
);

CREATE TABLE "user_tokens" (
    "user_id" TEXT NOT NULL,
    "login_provider" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "value" TEXT NULL,
    CONSTRAINT "PK_user_tokens" PRIMARY KEY ("user_id", "login_provider", "name"),
    CONSTRAINT "FK_user_tokens_users_user_id" FOREIGN KEY ("user_id") REFERENCES "users" ("id") ON DELETE CASCADE
);

CREATE TABLE "users" (
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
    "access_failed_count" INTEGER NOT NULL
);

CREATE TABLE "widget_placements" (
    "id" TEXT NOT NULL CONSTRAINT "pk_widget_placements" PRIMARY KEY,
    "desktop_layout_id" TEXT NOT NULL,
    "widget_key" TEXT NOT NULL,
    "grid_column" INTEGER NOT NULL,
    "grid_row" INTEGER NOT NULL,
    "width_units" INTEGER NOT NULL,
    "height_units" INTEGER NOT NULL,
    "revision" INTEGER NOT NULL,
    CONSTRAINT "fk_widget_placements_layout" FOREIGN KEY ("desktop_layout_id") REFERENCES "desktop_layouts" ("id") ON DELETE CASCADE
);;

CREATE UNIQUE INDEX "ux_agent_capabilities_agent_name" ON "agent_capabilities" ("agent_id", "capability_name");

CREATE INDEX "ix_agent_commands_agent_state_created" ON "agent_commands" ("agent_id", "state", "created_at_utc");

CREATE UNIQUE INDEX "ux_agent_commands_agent_operation_key" ON "agent_commands" ("agent_id", "operation_key");

CREATE INDEX "IX_agent_enrollment_tokens_redeemed_by_agent_id" ON "agent_enrollment_tokens" ("redeemed_by_agent_id");

CREATE UNIQUE INDEX "ux_agent_enrollment_tokens_hash" ON "agent_enrollment_tokens" ("token_hash");

CREATE INDEX "ix_agent_metric_samples_agent_metric_time" ON "agent_metric_samples" ("agent_id", "metric_name", "observed_at_utc");

CREATE INDEX "ix_agents_machine_identity" ON "agents" ("machine_identity");

CREATE UNIQUE INDEX "ux_application_definitions_package_stable_key" ON "application_definitions" ("owning_package_id", "stable_key");

CREATE INDEX "ix_audit_events_occurred_at_utc" ON "audit_events" ("occurred_at_utc");

CREATE INDEX "IX_authentication_setup_administrator_user_id" ON "authentication_setup" ("administrator_user_id");

CREATE UNIQUE INDEX "ux_desktop_layouts_default_per_viewport" ON "desktop_layouts" ("user_id", "viewport_class") WHERE is_default;

CREATE UNIQUE INDEX "ux_desktop_layouts_user_viewport_name" ON "desktop_layouts" ("user_id", "viewport_class", "name");

CREATE INDEX "IX_desktop_windows_application_definition_id" ON "desktop_windows" ("application_definition_id");

CREATE INDEX "IX_desktop_windows_launch_target_id" ON "desktop_windows" ("launch_target_id");

CREATE INDEX "IX_desktop_windows_session_reference_id" ON "desktop_windows" ("session_reference_id");

CREATE UNIQUE INDEX "ux_desktop_windows_layout_z_index" ON "desktop_windows" ("desktop_layout_id", "z_index");

CREATE INDEX "IX_launch_targets_application_definition_id" ON "launch_targets" ("application_definition_id");

CREATE UNIQUE INDEX "ux_launch_targets_package_external_identity" ON "launch_targets" ("owning_package_id", "external_identity");

CREATE UNIQUE INDEX "ux_notifications_user_deduplication" ON "notifications" ("user_id", "deduplication_key");

CREATE INDEX "ix_operation_progress_events_order" ON "operation_progress_events" ("operation_id", "occurred_at_utc", "id");

CREATE INDEX "ix_operations_owner_created_at_utc" ON "operations" ("owner_user_id", "created_at_utc");

CREATE UNIQUE INDEX "ux_operations_owner_idempotency" ON "operations" ("owner_user_id", "idempotency_key");

CREATE UNIQUE INDEX "ux_permission_assignments_grant" ON "permission_assignments" ("subject_kind", "subject_id", "permission", "scope_kind", "scope_id");

CREATE UNIQUE INDEX "ux_problems_identity" ON "problems" ("source_package_id", "problem_type", "stable_resource_identity");

CREATE INDEX "ix_remote_sessions_owner_package_page" ON "remote_sessions" ("owner_user_id", "caller_package_id", "id");

CREATE UNIQUE INDEX "ux_remote_sessions_owner_package_operation" ON "remote_sessions" ("owner_user_id", "caller_package_id", "operation_key");

CREATE UNIQUE INDEX "ux_remote_sessions_runtime" ON "remote_sessions" ("runtime_id") WHERE runtime_id IS NOT NULL;

CREATE INDEX "IX_role_claims_role_id" ON "role_claims" ("role_id");

CREATE UNIQUE INDEX "ux_roles_normalized_name" ON "roles" ("normalized_name");

CREATE INDEX "ix_secret_references_scope_purpose" ON "secret_references" ("owning_scope_type", "owning_scope_id", "purpose");

CREATE INDEX "IX_user_claims_user_id" ON "user_claims" ("user_id");

CREATE INDEX "IX_user_logins_user_id" ON "user_logins" ("user_id");

CREATE INDEX "IX_user_roles_role_id" ON "user_roles" ("role_id");

CREATE INDEX "ix_users_normalized_email" ON "users" ("normalized_email");

CREATE UNIQUE INDEX "ux_users_normalized_user_name" ON "users" ("normalized_user_name");

CREATE INDEX "IX_widget_placements_desktop_layout_id" ON "widget_placements" ("desktop_layout_id");

-- Seed rows the Core model declares with HasData.
INSERT INTO "authentication_setup" ("id", "completed_at_utc", "administrator_user_id")
VALUES (1, NULL, NULL);
