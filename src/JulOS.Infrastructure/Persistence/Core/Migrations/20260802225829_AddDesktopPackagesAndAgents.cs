using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddDesktopPackagesAndAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_commands",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    command_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_commands", x => x.id);
                    table.CheckConstraint("ck_agent_commands_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_agent_commands_revision", "revision >= 1");
                    table.CheckConstraint("ck_agent_commands_state", "state IN ('queued', 'running', 'succeeded', 'failed', 'expired', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_agent_commands_agent",
                        column: x => x.agent_id,
                        principalSchema: "core",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_credentials",
                schema: "core",
                columns: table => new
                {
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_credentials", x => x.agent_id);
                    table.CheckConstraint("ck_agent_credentials_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "fk_agent_credentials_agent",
                        column: x => x.agent_id,
                        principalSchema: "core",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_enrollment_tokens",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    redeemed_by_agent_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_enrollment_tokens", x => x.id);
                    table.CheckConstraint("ck_agent_enrollment_tokens_expiry", "expires_at_utc > created_at_utc");
                    table.CheckConstraint("ck_agent_enrollment_tokens_redemption", "(redeemed_at_utc IS NULL AND redeemed_by_agent_id IS NULL) OR (redeemed_at_utc IS NOT NULL AND redeemed_by_agent_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_agent_enrollment_tokens_agent",
                        column: x => x.redeemed_by_agent_id,
                        principalSchema: "core",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agent_metric_samples",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    labels_json = table.Column<string>(type: "jsonb", nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_metric_samples", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_metric_samples_agent",
                        column: x => x.agent_id,
                        principalSchema: "core",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_commands_agent_state_created",
                schema: "core",
                table: "agent_commands",
                columns: new[] { "agent_id", "state", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_commands_agent_operation_key",
                schema: "core",
                table: "agent_commands",
                columns: new[] { "agent_id", "operation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_enrollment_tokens_redeemed_by_agent_id",
                schema: "core",
                table: "agent_enrollment_tokens",
                column: "redeemed_by_agent_id");

            migrationBuilder.CreateIndex(
                name: "ux_agent_enrollment_tokens_hash",
                schema: "core",
                table: "agent_enrollment_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_metric_samples_agent_metric_time",
                schema: "core",
                table: "agent_metric_samples",
                columns: new[] { "agent_id", "metric_name", "observed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_commands",
                schema: "core");

            migrationBuilder.DropTable(
                name: "agent_credentials",
                schema: "core");

            migrationBuilder.DropTable(
                name: "agent_enrollment_tokens",
                schema: "core");

            migrationBuilder.DropTable(
                name: "agent_metric_samples",
                schema: "core");
        }
    }
}
