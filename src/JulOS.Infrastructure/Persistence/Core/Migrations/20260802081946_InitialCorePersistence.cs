using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCorePersistence : Migration
    {
        private static readonly string[] AgentCapabilityIdentityColumns = ["agent_id", "capability_name"];
        private static readonly string[] ApplicationDefinitionIdentityColumns = ["owning_package_id", "stable_key"];
        private static readonly string[] DefaultDesktopLayoutIdentityColumns = ["user_id", "viewport_class"];
        private static readonly string[] DesktopLayoutIdentityColumns = ["user_id", "viewport_class", "name"];
        private static readonly string[] DesktopWindowZIndexColumns = ["desktop_layout_id", "z_index"];
        private static readonly string[] LaunchTargetIdentityColumns = ["owning_package_id", "external_identity"];
        private static readonly string[] NotificationDeduplicationColumns = ["user_id", "deduplication_key"];
        private static readonly string[] PermissionAssignmentIdentityColumns = ["subject_kind", "subject_id", "permission", "scope_kind", "scope_id"];
        private static readonly string[] ProblemIdentityColumns = ["source_package_id", "problem_type", "stable_resource_identity"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "agents",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    machine_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operating_system = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    architecture = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    version = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    enrolled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                    table.CheckConstraint("ck_agents_revision", "revision >= 1");
                    table.CheckConstraint("ck_agents_revoked_at", "(state = 'Revoked' AND revoked_at_utc IS NOT NULL) OR (state <> 'Revoked' AND revoked_at_utc IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "application_definitions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owning_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    stable_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    instance_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    default_width = table.Column<int>(type: "integer", nullable: false),
                    default_height = table.Column<int>(type: "integer", nullable: false),
                    minimum_width = table.Column<int>(type: "integer", nullable: false),
                    minimum_height = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_definitions", x => x.id);
                    table.CheckConstraint("ck_application_definitions_revision", "revision >= 1");
                    table.CheckConstraint("ck_application_definitions_window_size", "minimum_width BETWEEN 120 AND 16384 AND minimum_height BETWEEN 120 AND 16384 AND default_width BETWEEN minimum_width AND 16384 AND default_height BETWEEN minimum_height AND 16384");
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    target_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    remote_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    safe_details = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "desktop_layouts",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewport_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desktop_layouts", x => x.id);
                    table.CheckConstraint("ck_desktop_layouts_revision", "revision >= 1");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    deduplication_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    action_link = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "package_installations",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    fault_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    fault_detail = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    faulted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_package_installations", x => x.id);
                    table.CheckConstraint("ck_package_installations_fault_metadata", "(state = 'Faulted' AND fault_code IS NOT NULL AND fault_detail IS NOT NULL AND faulted_at_utc IS NOT NULL) OR (state <> 'Faulted' AND fault_code IS NULL AND fault_detail IS NULL AND faulted_at_utc IS NULL)");
                    table.CheckConstraint("ck_package_installations_revision", "revision >= 1");
                });

            migrationBuilder.CreateTable(
                name: "permission_assignments",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    scope_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    scope_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_assignments", x => x.id);
                    table.CheckConstraint("ck_permission_assignments_scope", "(scope_kind = 'Global' AND scope_id IS NULL) OR (scope_kind <> 'Global' AND scope_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "problems",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    problem_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    stable_resource_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    first_detected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observation_count = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_problems", x => x.id);
                    table.CheckConstraint("ck_problems_observation_count", "observation_count >= 1");
                    table.CheckConstraint("ck_problems_revision", "revision >= 1");
                    table.CheckConstraint("ck_problems_state_timestamps", "(state = 'Active' AND acknowledged_at_utc IS NULL AND acknowledged_by_user_id IS NULL AND resolved_at_utc IS NULL) OR (state = 'Acknowledged' AND acknowledged_at_utc IS NOT NULL AND acknowledged_by_user_id IS NOT NULL AND resolved_at_utc IS NULL) OR (state = 'Resolved' AND resolved_at_utc IS NOT NULL) OR state = 'Suppressed'");
                });

            migrationBuilder.CreateTable(
                name: "session_references",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owning_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    session_kind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    target_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    lifecycle_policy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    connected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_references", x => x.id);
                    table.CheckConstraint("ck_session_references_ended_at", "(state = 'Ended' AND ended_at_utc IS NOT NULL) OR (state <> 'Ended' AND ended_at_utc IS NULL)");
                    table.CheckConstraint("ck_session_references_expiry", "expires_at_utc IS NULL OR expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_session_references_revision", "revision >= 1");
                });

            migrationBuilder.CreateTable(
                name: "agent_capabilities",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    capability_version = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    metadata_version = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_capabilities", x => x.id);
                    table.CheckConstraint("ck_agent_capabilities_metadata_length", "char_length(metadata) <= 8192");
                    table.CheckConstraint("ck_agent_capabilities_revision", "revision >= 1");
                    table.CheckConstraint("ck_agent_capabilities_version", "capability_version >= 1 AND metadata_version >= 1");
                    table.ForeignKey(
                        name: "fk_agent_capabilities_agent",
                        column: x => x.agent_id,
                        principalSchema: "core",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_viewports",
                schema: "core",
                columns: table => new
                {
                    application_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewport_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_viewports", x => new { x.application_definition_id, x.viewport_class });
                    table.ForeignKey(
                        name: "fk_application_viewports_application",
                        column: x => x.application_definition_id,
                        principalSchema: "core",
                        principalTable: "application_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "launch_targets",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owning_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    external_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    approval_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    first_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_launch_targets", x => x.id);
                    table.CheckConstraint("ck_launch_targets_approval", "(approval_state = 'Approved' AND approved_at_utc IS NOT NULL AND approved_by_user_id IS NOT NULL) OR (approval_state <> 'Approved' AND approved_at_utc IS NULL AND approved_by_user_id IS NULL)");
                    table.CheckConstraint("ck_launch_targets_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "fk_launch_targets_application",
                        column: x => x.application_definition_id,
                        principalSchema: "core",
                        principalTable: "application_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "widget_placements",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    desktop_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    grid_column = table.Column<int>(type: "integer", nullable: false),
                    grid_row = table.Column<int>(type: "integer", nullable: false),
                    width_units = table.Column<int>(type: "integer", nullable: false),
                    height_units = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widget_placements", x => x.id);
                    table.CheckConstraint("ck_widget_placements_grid", "grid_column >= 0 AND grid_row >= 0 AND width_units > 0 AND height_units > 0 AND grid_column + width_units <= 64 AND grid_row + height_units <= 64");
                    table.CheckConstraint("ck_widget_placements_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "fk_widget_placements_layout",
                        column: x => x.desktop_layout_id,
                        principalSchema: "core",
                        principalTable: "desktop_layouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "desktop_windows",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    desktop_layout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    launch_target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    restore_x = table.Column<int>(type: "integer", nullable: false),
                    restore_y = table.Column<int>(type: "integer", nullable: false),
                    restore_width = table.Column<int>(type: "integer", nullable: false),
                    restore_height = table.Column<int>(type: "integer", nullable: false),
                    z_index = table.Column<int>(type: "integer", nullable: false),
                    session_reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desktop_windows", x => x.id);
                    table.CheckConstraint("ck_desktop_windows_bounds", "width BETWEEN 1 AND 16384 AND height BETWEEN 1 AND 16384 AND restore_width BETWEEN 1 AND 16384 AND restore_height BETWEEN 1 AND 16384 AND abs(x) <= 65536 AND abs(y) <= 65536 AND abs(restore_x) <= 65536 AND abs(restore_y) <= 65536");
                    table.CheckConstraint("ck_desktop_windows_revision", "revision >= 1");
                    table.CheckConstraint("ck_desktop_windows_z_index", "z_index >= 0");
                    table.ForeignKey(
                        name: "fk_desktop_windows_application",
                        column: x => x.application_definition_id,
                        principalSchema: "core",
                        principalTable: "application_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desktop_windows_launch_target",
                        column: x => x.launch_target_id,
                        principalSchema: "core",
                        principalTable: "launch_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_desktop_windows_layout",
                        column: x => x.desktop_layout_id,
                        principalSchema: "core",
                        principalTable: "desktop_layouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_desktop_windows_session",
                        column: x => x.session_reference_id,
                        principalSchema: "core",
                        principalTable: "session_references",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION core.reject_audit_event_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION USING
                        ERRCODE = 'check_violation',
                        CONSTRAINT = 'audit_event_is_append_only',
                        MESSAGE = 'Audit events are append-only.';
                END;
                $function$;

                CREATE TRIGGER audit_event_is_append_only
                BEFORE UPDATE OR DELETE ON core.audit_events
                FOR EACH ROW EXECUTE FUNCTION core.reject_audit_event_change();
                """);

            migrationBuilder.CreateIndex(
                name: "ux_agent_capabilities_agent_name",
                schema: "core",
                table: "agent_capabilities",
                columns: AgentCapabilityIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agents_machine_identity",
                schema: "core",
                table: "agents",
                column: "machine_identity");

            migrationBuilder.CreateIndex(
                name: "ux_application_definitions_package_stable_key",
                schema: "core",
                table: "application_definitions",
                columns: ApplicationDefinitionIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at_utc",
                schema: "core",
                table: "audit_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_desktop_layouts_default_per_viewport",
                schema: "core",
                table: "desktop_layouts",
                columns: DefaultDesktopLayoutIdentityColumns,
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ux_desktop_layouts_user_viewport_name",
                schema: "core",
                table: "desktop_layouts",
                columns: DesktopLayoutIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_desktop_windows_application_definition_id",
                schema: "core",
                table: "desktop_windows",
                column: "application_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_desktop_windows_launch_target_id",
                schema: "core",
                table: "desktop_windows",
                column: "launch_target_id");

            migrationBuilder.CreateIndex(
                name: "IX_desktop_windows_session_reference_id",
                schema: "core",
                table: "desktop_windows",
                column: "session_reference_id");

            migrationBuilder.CreateIndex(
                name: "ux_desktop_windows_layout_z_index",
                schema: "core",
                table: "desktop_windows",
                columns: DesktopWindowZIndexColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_launch_targets_application_definition_id",
                schema: "core",
                table: "launch_targets",
                column: "application_definition_id");

            migrationBuilder.CreateIndex(
                name: "ux_launch_targets_package_external_identity",
                schema: "core",
                table: "launch_targets",
                columns: LaunchTargetIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_notifications_user_deduplication",
                schema: "core",
                table: "notifications",
                columns: NotificationDeduplicationColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_permission_assignments_grant",
                schema: "core",
                table: "permission_assignments",
                columns: PermissionAssignmentIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_problems_identity",
                schema: "core",
                table: "problems",
                columns: ProblemIdentityColumns,
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_widget_placements_desktop_layout_id",
                schema: "core",
                table: "widget_placements",
                column: "desktop_layout_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_capabilities",
                schema: "core");

            migrationBuilder.DropTable(
                name: "application_viewports",
                schema: "core");

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS audit_event_is_append_only ON core.audit_events;
                DROP FUNCTION IF EXISTS core.reject_audit_event_change();
                """);

            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "core");

            migrationBuilder.DropTable(
                name: "desktop_windows",
                schema: "core");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "core");

            migrationBuilder.DropTable(
                name: "package_installations",
                schema: "core");

            migrationBuilder.DropTable(
                name: "permission_assignments",
                schema: "core");

            migrationBuilder.DropTable(
                name: "problems",
                schema: "core");

            migrationBuilder.DropTable(
                name: "widget_placements",
                schema: "core");

            migrationBuilder.DropTable(
                name: "agents",
                schema: "core");

            migrationBuilder.DropTable(
                name: "launch_targets",
                schema: "core");

            migrationBuilder.DropTable(
                name: "session_references",
                schema: "core");

            migrationBuilder.DropTable(
                name: "desktop_layouts",
                schema: "core");

            migrationBuilder.DropTable(
                name: "application_definitions",
                schema: "core");
        }
    }
}
