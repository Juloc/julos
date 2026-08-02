using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace JulOS.Integration.Tests.Persistence;

[TestClass]
[DoNotParallelize]
public sealed class CoreMigrationTests
{
    [TestMethod]
    public async Task EmptyDatabaseMigratesToTheCommittedCoreSchema()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);

        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var tableNames = new List<string>();
        await using (var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'core' ORDER BY table_name",
            connection))
        await using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        CollectionAssert.Contains(tableNames, "package_installations");
        CollectionAssert.Contains(tableNames, "desktop_layouts");
        CollectionAssert.Contains(tableNames, "session_references");
        CollectionAssert.Contains(tableNames, "audit_events");
        CollectionAssert.Contains(tableNames, "secret_references");

        await using var migrationCommand = new NpgsqlCommand(
            "SELECT count(*) FROM core.__ef_migrations_history",
            connection);
        var applied = Convert.ToInt32(await migrationCommand.ExecuteScalarAsync().ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsTrue(applied > 0, "At least one migration must be recorded.");
    }

    [TestMethod]
    public async Task ExistingAdministratorReceivesExplicitAuthorizationGrantsOnUpgrade()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(options, database.ConnectionString);

        await using (var context = new CoreDbContext(options.Options))
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator
                .MigrateAsync("20260802121608_AddLocalAuthentication")
                .ConfigureAwait(false);
        }

        var administratorUserId = Guid.CreateVersion7();
        var administratorRoleId = Guid.CreateVersion7();

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO core.roles
                    (id, is_system_role, revision, name, normalized_name, concurrency_stamp)
                VALUES
                    (@role_id, TRUE, 1, 'Administrator', 'ADMINISTRATOR', 'role-stamp');

                INSERT INTO core.users
                    (id, display_name, preferred_language, time_zone, theme,
                     created_at_utc, updated_at_utc, revision,
                     user_name, normalized_user_name, email, normalized_email,
                     email_confirmed, password_hash, security_stamp, concurrency_stamp,
                     phone_number, phone_number_confirmed, two_factor_enabled,
                     lockout_end_utc, lockout_enabled, access_failed_count)
                VALUES
                    (@user_id, 'Administrator', 'en', 'UTC', 'system',
                     CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 1,
                     'admin', 'ADMIN', NULL, NULL,
                     FALSE, NULL, 'security-stamp', 'user-stamp',
                     NULL, FALSE, FALSE, NULL, TRUE, 0);

                INSERT INTO core.user_roles (user_id, role_id)
                VALUES (@user_id, @role_id);

                UPDATE core.authentication_setup
                SET administrator_user_id = @user_id,
                    completed_at_utc = CURRENT_TIMESTAMP
                WHERE id = 1;
                """,
                connection);
            command.Parameters.AddWithValue("user_id", administratorUserId);
            command.Parameters.AddWithValue("role_id", administratorRoleId);
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);

        await using var verification = new NpgsqlConnection(database.ConnectionString);
        await verification.OpenAsync().ConfigureAwait(false);

        await using var descriptionCommand = new NpgsqlCommand(
            "SELECT description FROM core.roles WHERE id = @role_id",
            verification);
        descriptionCommand.Parameters.AddWithValue("role_id", administratorRoleId);
        Assert.AreEqual(
            "Full control of the JulOS installation.",
            Convert.ToString(
                await descriptionCommand.ExecuteScalarAsync().ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture));

        await using var grantCommand = new NpgsqlCommand(
            """
            SELECT count(*)
            FROM core.permission_assignments
            WHERE subject_kind = 'Role'
              AND subject_id = @role_id
              AND scope_kind = 'Global'
              AND scope_id IS NULL
              AND permission IN (
                  'core.system.version.read',
                  'core.authorization.read',
                  'core.authorization.manage',
                  'core.operation.create',
                  'core.operation.read',
                  'core.operation.cancel',
                  'core.secret.read',
                  'core.secret.manage')
            """,
            verification);
        grantCommand.Parameters.AddWithValue("role_id", administratorRoleId);
        Assert.AreEqual(
            8L,
            Convert.ToInt64(
                await grantCommand.ExecuteScalarAsync().ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task DatabaseConstraintsRejectStatesTheDomainCannotRepresent()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await AssertConstraintViolationAsync(
            connection,
            """
            INSERT INTO core.package_installations
                (id, package_id, state, revision, fault_code, fault_detail, faulted_at_utc)
            VALUES
                (@id, 'de.juloc.invalid', 'Faulted', 1, NULL, NULL, NULL)
            """,
            "ck_package_installations_fault_metadata").ConfigureAwait(false);

        await AssertConstraintViolationAsync(
            connection,
            """
            INSERT INTO core.permission_assignments
                (id, subject_kind, subject_id, permission, scope_kind, scope_id, granted_at_utc, granted_by_user_id)
            VALUES
                (@id, 'User', @subject, 'package.read', 'Global', 'must-be-null', now(), @granter)
            """,
            "ck_permission_assignments_scope").ConfigureAwait(false);

        await AssertConstraintViolationAsync(
            connection,
            """
            INSERT INTO core.agents
                (id, name, machine_identity, operating_system, architecture, version, state,
                 enrolled_at_utc, last_seen_at_utc, revoked_at_utc, revision)
            VALUES
                (@id, 'Agent', 'machine', 'Linux', 'x64', '1.0.0', 'Revoked', now(), NULL, NULL, 1)
            """,
            "ck_agents_revoked_at").ConfigureAwait(false);

        await AssertConstraintViolationAsync(
            connection,
            """
            INSERT INTO core.secret_references
                (id, owning_scope_type, owning_scope_id, purpose, storage_provider,
                 encryption_key_id, nonce, ciphertext, authentication_tag,
                 created_at_utc, rotated_at_utc, deleted_at_utc, revision)
            VALUES
                (@id, 'Package', 'de.juloc.invalid', 'remote.password', 'core-aes-gcm-v1',
                 NULL, NULL, NULL, NULL, now(), NULL, NULL, 1)
            """,
            "ck_secret_references_protected_value").ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AuditEventsCannotBeChangedOrDeletedAfterInsert()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync().ConfigureAwait(false);
        await CoreDatabaseMigrator.MigrateAsync(database.ConnectionString).ConfigureAwait(false);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        var id = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO core.audit_events
                (id, occurred_at_utc, user_id, agent_id, source_package_id, action, target_type,
                 target_id, outcome, correlation_id, remote_address, summary, safe_details)
            VALUES
                (@id, now(), NULL, NULL, NULL, 'package.enable', 'package', 'de.juloc.example',
                 'Succeeded', 'test-correlation', NULL, 'Enabled package', '{}')
            """,
            connection))
        {
            insert.Parameters.AddWithValue("id", id);
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var updateError = await Assert.ThrowsExactlyAsync<PostgresException>(async () =>
        {
            await using var update = new NpgsqlCommand(
                "UPDATE core.audit_events SET summary = 'changed' WHERE id = @id",
                connection);
            update.Parameters.AddWithValue("id", id);
            await update.ExecuteNonQueryAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
        Assert.AreEqual("audit_event_is_append_only", updateError.ConstraintName);

        var deleteError = await Assert.ThrowsExactlyAsync<PostgresException>(async () =>
        {
            await using var delete = new NpgsqlCommand("DELETE FROM core.audit_events WHERE id = @id", connection);
            delete.Parameters.AddWithValue("id", id);
            await delete.ExecuteNonQueryAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
        Assert.AreEqual("audit_event_is_append_only", deleteError.ConstraintName);
    }

    private static async Task AssertConstraintViolationAsync(
        NpgsqlConnection connection,
        string sql,
        string expectedConstraint)
    {
        var exception = await Assert.ThrowsExactlyAsync<PostgresException>(async () =>
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());

            if (sql.Contains("@subject", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("subject", Guid.CreateVersion7());
                command.Parameters.AddWithValue("granter", Guid.CreateVersion7());
            }

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        Assert.AreEqual(expectedConstraint, exception.ConstraintName);
    }
}
