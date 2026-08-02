using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JulOS.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAuthorization : Migration
    {
        private static readonly string[] PermissionGrantIndexColumns =
            ["subject_kind", "subject_id", "permission", "scope_kind", "scope_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_permission_assignments_grant",
                schema: "core",
                table: "permission_assignments");

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "core",
                table: "roles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ux_permission_assignments_grant",
                schema: "core",
                table: "permission_assignments",
                columns: PermissionGrantIndexColumns,
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.Sql(
                """
                UPDATE core.roles
                SET description = 'Full control of the JulOS installation.'
                WHERE normalized_name = 'ADMINISTRATOR';

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
                        ('core.system.version.read'),
                        ('core.authorization.read'),
                        ('core.authorization.manage')
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
                      'core.system.version.read',
                      'core.authorization.read',
                      'core.authorization.manage')
                  AND assignment.scope_kind = 'Global'
                  AND assignment.scope_id IS NULL
                  AND assignment.granted_by_user_id = setup.administrator_user_id
                  AND role.normalized_name = 'ADMINISTRATOR'
                  AND setup.id = 1;
                """);

            migrationBuilder.DropIndex(
                name: "ux_permission_assignments_grant",
                schema: "core",
                table: "permission_assignments");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "core",
                table: "roles");

            migrationBuilder.CreateIndex(
                name: "ux_permission_assignments_grant",
                schema: "core",
                table: "permission_assignments",
                columns: PermissionGrantIndexColumns,
                unique: true);
        }
    }
}
