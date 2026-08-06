using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableRemoteSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "remote_sessions",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caller_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_identity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    target_port = table.Column<int>(type: "integer", nullable: false),
                    secret_reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    network_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    viewport_width = table.Column<int>(type: "integer", nullable: false),
                    viewport_height = table.Column<int>(type: "integer", nullable: false),
                    device_scale_factor = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    idle_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    maximum_session_seconds = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    connected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    runtime_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    display_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    display_contract_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    display_endpoint = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    display_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    failure_detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    failure_retryable = table.Column<bool>(type: "boolean", nullable: true),
                    cancellation_operation_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remote_sessions", x => x.id);
                    table.CheckConstraint("ck_remote_sessions_cancellation", "(state = 'cancelled' AND cancellation_operation_key IS NOT NULL) OR (state <> 'cancelled' AND cancellation_operation_key IS NULL AND cancellation_reason IS NULL)");
                    table.CheckConstraint("ck_remote_sessions_display", "(display_kind IS NULL AND display_contract_version IS NULL AND display_endpoint IS NULL AND display_expires_at_utc IS NULL) OR (state = 'connected' AND display_kind IS NOT NULL AND display_contract_version IS NOT NULL AND display_endpoint IS NOT NULL AND display_expires_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_remote_sessions_failure", "(state = 'failed' AND failure_code IS NOT NULL AND failure_detail IS NOT NULL AND failure_retryable IS NOT NULL) OR (state <> 'failed' AND failure_code IS NULL AND failure_detail IS NULL AND failure_retryable IS NULL)");
                    table.CheckConstraint("ck_remote_sessions_revision", "revision >= 1");
                    table.CheckConstraint("ck_remote_sessions_state", "state IN ('requested', 'provisioning', 'connecting', 'connected', 'disconnecting', 'disconnected', 'cancelled', 'expired', 'failed')");
                    table.CheckConstraint("ck_remote_sessions_target_port", "target_port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_remote_sessions_terminal_time", "(state IN ('disconnected', 'cancelled', 'expired', 'failed') AND ended_at_utc IS NOT NULL) OR (state NOT IN ('disconnected', 'cancelled', 'expired', 'failed') AND ended_at_utc IS NULL)");
                    table.CheckConstraint("ck_remote_sessions_timeouts", "idle_timeout_seconds BETWEEN 60 AND 86400 AND maximum_session_seconds BETWEEN 300 AND 604800 AND idle_timeout_seconds <= maximum_session_seconds");
                    table.CheckConstraint("ck_remote_sessions_timestamps", "updated_at_utc >= created_at_utc AND last_activity_at_utc >= created_at_utc AND expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_remote_sessions_viewport", "viewport_width BETWEEN 320 AND 7680 AND viewport_height BETWEEN 240 AND 4320 AND device_scale_factor BETWEEN 0.5 AND 4");
                    table.ForeignKey(
                        name: "fk_remote_sessions_owner",
                        column: x => x.owner_user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_remote_sessions_owner_package_page",
                schema: "core",
                table: "remote_sessions",
                columns: new[] { "owner_user_id", "caller_package_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ux_remote_sessions_owner_package_operation",
                schema: "core",
                table: "remote_sessions",
                columns: new[] { "owner_user_id", "caller_package_id", "operation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_remote_sessions_runtime",
                schema: "core",
                table: "remote_sessions",
                column: "runtime_id",
                unique: true,
                filter: "runtime_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remote_sessions",
                schema: "core");
        }
    }
}
