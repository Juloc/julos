using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSecretReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "secret_references",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owning_scope_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owning_scope_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    storage_provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encryption_key_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: true),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    authentication_tag = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secret_references", x => x.id);
                    table.CheckConstraint("ck_secret_references_deletion", "deleted_at_utc IS NULL OR deleted_at_utc >= created_at_utc");
                    table.CheckConstraint("ck_secret_references_protected_value", "(deleted_at_utc IS NULL AND encryption_key_id IS NOT NULL AND nonce IS NOT NULL AND octet_length(nonce) = 12 AND ciphertext IS NOT NULL AND octet_length(ciphertext) > 0 AND authentication_tag IS NOT NULL AND octet_length(authentication_tag) = 16) OR (deleted_at_utc IS NOT NULL AND encryption_key_id IS NULL AND nonce IS NULL AND ciphertext IS NULL AND authentication_tag IS NULL)");
                    table.CheckConstraint("ck_secret_references_revision", "revision >= 1");
                    table.CheckConstraint("ck_secret_references_rotation", "rotated_at_utc IS NULL OR rotated_at_utc >= created_at_utc");
                    table.CheckConstraint("ck_secret_references_scope", "(owning_scope_type = 'System' AND owning_scope_id IS NULL) OR (owning_scope_type = 'Package' AND owning_scope_id IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_secret_references_scope_purpose",
                schema: "core",
                table: "secret_references",
                columns: new[] { "owning_scope_type", "owning_scope_id", "purpose" });

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
                ('core.secret.read'),
                ('core.secret.manage')
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
              'core.secret.read',
              'core.secret.manage')
          AND assignment.scope_kind = 'Global'
          AND assignment.scope_id IS NULL
          AND assignment.granted_by_user_id = setup.administrator_user_id
          AND role.normalized_name = 'ADMINISTRATOR'
          AND setup.id = 1;
        """);

            migrationBuilder.DropTable(
                name: "secret_references",
                schema: "core");
        }
    }
}
