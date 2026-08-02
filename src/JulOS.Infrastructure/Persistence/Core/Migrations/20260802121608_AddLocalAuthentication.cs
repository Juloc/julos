using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalAuthentication : Migration
    {
        private static readonly string[] AuthenticationSetupSeedColumns =
            ["id", "administrator_user_id", "completed_at_utc"];
        private static readonly object[] AuthenticationSetupSeedValues = [1, null, null];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roles",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                    table.CheckConstraint("ck_roles_revision", "revision >= 1");
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    preferred_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    user_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_language", "char_length(preferred_language) BETWEEN 2 AND 32");
                    table.CheckConstraint("ck_users_revision", "revision >= 1");
                    table.CheckConstraint("ck_users_theme", "theme IN ('system', 'light', 'dark')");
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "core",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "authentication_setup",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    administrator_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authentication_setup", x => x.id);
                    table.CheckConstraint("ck_authentication_setup_completion", "(completed_at_utc IS NULL AND administrator_user_id IS NULL) OR (completed_at_utc IS NOT NULL AND administrator_user_id IS NOT NULL)");
                    table.CheckConstraint("ck_authentication_setup_singleton", "id = 1");
                    table.ForeignKey(
                        name: "fk_authentication_setup_administrator",
                        column: x => x.administrator_user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "core",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    provider_display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "FK_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "core",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "core",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "core",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "FK_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "core",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "authentication_setup",
                columns: AuthenticationSetupSeedColumns,
                values: AuthenticationSetupSeedValues);

            migrationBuilder.CreateIndex(
                name: "IX_authentication_setup_administrator_user_id",
                schema: "core",
                table: "authentication_setup",
                column: "administrator_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_role_claims_role_id",
                schema: "core",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ux_roles_normalized_name",
                schema: "core",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_user_id",
                schema: "core",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_user_id",
                schema: "core",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_role_id",
                schema: "core",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                schema: "core",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_user_name",
                schema: "core",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authentication_setup",
                schema: "core");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "core");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "core");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "core");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "core");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "core");

            migrationBuilder.DropTable(
                name: "users",
                schema: "core");
        }
    }
}
