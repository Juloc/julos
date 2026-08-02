using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operations",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_package_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    target_reference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    progress_percent = table.Column<int>(type: "integer", nullable: true),
                    current_step = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    failure_detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    cancellation_requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                    table.CheckConstraint("ck_operations_lifecycle", "(state = 'Queued' AND started_at_utc IS NULL AND completed_at_utc IS NULL AND failure_code IS NULL AND failure_detail IS NULL) OR (state = 'Running' AND started_at_utc IS NOT NULL AND completed_at_utc IS NULL AND failure_code IS NULL AND failure_detail IS NULL) OR (state = 'Succeeded' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NULL AND failure_detail IS NULL) OR (state = 'Failed' AND started_at_utc IS NOT NULL AND completed_at_utc IS NOT NULL AND failure_code IS NOT NULL AND failure_detail IS NOT NULL) OR (state = 'Cancelled' AND completed_at_utc IS NOT NULL AND failure_code IS NULL AND failure_detail IS NULL)");
                    table.CheckConstraint("ck_operations_progress", "progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_operations_revision", "revision >= 1");
                    table.CheckConstraint("ck_operations_state", "state IN ('Queued', 'Running', 'Succeeded', 'Failed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_operations_owner_user",
                        column: x => x.owner_user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "operation_progress_events",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    progress_percent = table.Column<int>(type: "integer", nullable: true),
                    current_step = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operation_progress_events", x => x.id);
                    table.CheckConstraint("ck_operation_progress_events_progress", "progress_percent IS NULL OR progress_percent BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_operation_progress_events_operation",
                        column: x => x.operation_id,
                        principalSchema: "core",
                        principalTable: "operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_operation_progress_events_order",
                schema: "core",
                table: "operation_progress_events",
                columns: new[] { "operation_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_operations_owner_created_at_utc",
                schema: "core",
                table: "operations",
                columns: new[] { "owner_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_operations_owner_idempotency",
                schema: "core",
                table: "operations",
                columns: new[] { "owner_user_id", "idempotency_key" },
                unique: true);

    migrationBuilder.Sql(
        """
        INSERT INTO core.permission_assignments
            (id, subject_kind, subject_id, permission, scope_kind, scope_id,
             granted_at_utc, granted_by_user_id)
        SELECT
            uuidv7(),
            'Role',
            role.id,
            permission.name,
            'Global',
            NULL,
            CURRENT_TIMESTAMP,
            setup.administrator_user_id
        FROM core.roles AS role
        CROSS JOIN core.authentication_setup AS setup
        CROSS JOIN (
            VALUES
                ('core.operation.create'),
                ('core.operation.read'),
                ('core.operation.cancel')
        ) AS permission(name)
        WHERE role.normalized_name = 'ADMINISTRATOR'
          AND setup.id = 1
          AND setup.administrator_user_id IS NOT NULL
          AND NOT EXISTS (
              SELECT 1
              FROM core.permission_assignments AS existing
              WHERE existing.subject_kind = 'Role'
                AND existing.subject_id = role.id
                AND existing.permission = permission.name
                AND existing.scope_kind = 'Global'
                AND existing.scope_id IS NULL
          );
        """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

    migrationBuilder.Sql(
        """
        DELETE FROM core.permission_assignments AS assignment
        USING core.roles AS role, core.authentication_setup AS setup
        WHERE assignment.subject_kind = 'Role'
          AND assignment.subject_id = role.id
          AND assignment.permission IN (
              'core.operation.create',
              'core.operation.read',
              'core.operation.cancel')
          AND assignment.scope_kind = 'Global'
          AND assignment.scope_id IS NULL
          AND assignment.granted_by_user_id = setup.administrator_user_id
          AND role.normalized_name = 'ADMINISTRATOR'
          AND setup.id = 1;
        """);

            migrationBuilder.DropTable(
                name: "operation_progress_events",
                schema: "core");

            migrationBuilder.DropTable(
                name: "operations",
                schema: "core");
        }
    }
}
