using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Tests.Persistence;

/// <summary>
/// DB-001: the ordered SQLite schema migrations replace <c>EnsureCreated</c> as the
/// supported upgrade path. These tests use real SQLite files, never an in-memory
/// approximation, because the behaviour under test is the on-disk schema itself.
/// </summary>
[TestClass]
public sealed class SqliteSchemaMigrationTests
{
    private readonly List<string> directories = [];

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var directory in this.directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file the operating system still holds open is temporary test litter, not a failure.
            }
        }
    }

    [TestMethod]
    public async Task FreshDatabaseAppliesEveryMigrationInOrder()
    {
        var connectionString = this.CreateDatabasePath();

        var applied = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        CollectionAssert.AreEqual(
            SqliteSchemaCatalog.Migrations.Select(migration => migration.Id).ToArray(),
            applied.ToArray(),
            "A fresh database must apply every shipped migration in catalog order.");
    }

    [TestMethod]
    public async Task UpgradedPreviousReleaseDatabaseMatchesAFreshDatabase()
    {
        var fresh = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(fresh);

        var upgraded = this.CreateDatabasePath();
        await ExecuteAsync(upgraded, SqliteSchemaCatalog.Baseline.Sql);
        var applied = await SqliteSchemaMigrationRunner.MigrateAsync(upgraded);

        Assert.IsFalse(
            applied.Contains(SqliteSchemaCatalog.Baseline.Id),
            "A recognised previous-release database is baselined, not recreated.");
        Assert.AreEqual(
            await ReadSchemaAsync(fresh),
            await ReadSchemaAsync(upgraded),
            "A fresh database and an upgraded previous-release database must expose the same model.");
    }

    [TestMethod]
    public async Task RealPreviousReleaseFixtureUpgradesAndKeepsItsRows()
    {
        // tests/fixtures/sqlite/core-0.4.0-beta.42.db was produced by building the
        // v0.4.0-beta.42 tag and letting that release's own EnsureCreated path create the
        // schema. It is therefore independent of the baseline script generated from the
        // current model, which is what makes this an upgrade test rather than a tautology.
        var connectionString = this.CopyFixture("core-0.4.0-beta.42.db");

        var applied = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        Assert.IsFalse(
            applied.Contains(SqliteSchemaCatalog.Baseline.Id),
            "A real previous-release database must be baselined, not recreated.");
        CollectionAssert.Contains(
            applied.ToArray(),
            "0002_core_check_constraints",
            "The parity migration must still be applied to a previous-release database.");

        Assert.AreEqual(4L, await ScalarAsync(connectionString, "SELECT revision FROM users WHERE user_name = 'admin';"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM roles;"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM permission_assignments;"));
        Assert.AreEqual(7L, await ScalarAsync(connectionString, "SELECT revision FROM package_installations;"));
        Assert.AreEqual(12L, await ScalarAsync(connectionString, "SELECT revision FROM agents;"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM agent_capabilities;"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM agent_metric_samples;"));
        Assert.AreEqual(5L, await ScalarAsync(connectionString, "SELECT revision FROM desktop_layouts;"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM widget_placements;"));
        Assert.AreEqual(6L, await ScalarAsync(connectionString, "SELECT revision FROM session_references;"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM secret_references;"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM audit_events;"));

        var fresh = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(fresh);
        Assert.AreEqual(
            await ReadSchemaAsync(fresh),
            await ReadSchemaAsync(connectionString),
            "The upgraded previous-release database must expose the current model.");
    }

    [TestMethod]
    public async Task MigratingTwiceIsIdempotent()
    {
        var connectionString = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        var schema = await ReadSchemaAsync(connectionString);

        var second = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        Assert.AreEqual(0, second.Count, "A migrated database has nothing left to apply.");
        Assert.AreEqual(schema, await ReadSchemaAsync(connectionString));
    }

    [TestMethod]
    public async Task PreviousReleaseRowsSurviveTheUpgrade()
    {
        var connectionString = this.CreateDatabasePath();
        await ExecuteAsync(connectionString, SqliteSchemaCatalog.Baseline.Sql);
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO desktop_layouts (id, user_id, viewport_class, name, is_default, revision, updated_at_utc)
            VALUES ('layout-1', 'user-1', 'desktop', 'Main', 1, 3, '2026-01-01 00:00:00');
            INSERT INTO widget_placements (id, desktop_layout_id, widget_key, grid_column, grid_row, width_units, height_units, revision)
            VALUES ('widget-1', 'layout-1', 'host-metrics', 0, 0, 2, 2, 1);
            """);

        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        Assert.AreEqual(3L, await ScalarAsync(connectionString, "SELECT revision FROM desktop_layouts WHERE id = 'layout-1';"));
        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM widget_placements WHERE desktop_layout_id = 'layout-1';"));
    }

    [TestMethod]
    public async Task UpgradedDatabaseEnforcesTheCheckConstraintsPostgreSqlEnforces()
    {
        var connectionString = this.CreateDatabasePath();
        await ExecuteAsync(connectionString, SqliteSchemaCatalog.Baseline.Sql);
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        var violation = await Assert.ThrowsExactlyAsync<SqliteException>(
            () => ExecuteAsync(
                connectionString,
                """
                INSERT INTO desktop_layouts (id, user_id, viewport_class, name, is_default, revision, updated_at_utc)
                VALUES ('layout-2', 'user-1', 'desktop', 'Invalid', 1, 0, '2026-01-01 00:00:00');
                """));

        StringAssert.Contains(
            violation.Message,
            "ck_desktop_layouts_revision",
            StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task MigratedDatabaseEnforcesAppendOnlyAuditEvents()
    {
        var connectionString = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO audit_events (id, occurred_at_utc, user_id, agent_id, source_package_id, action,
                                      target_type, target_id, outcome, correlation_id, remote_address, summary, safe_details)
            VALUES ('audit-1', '2026-01-01 00:00:00', NULL, NULL, NULL, 'package.enable',
                    'package', 'de.juloc.julos.reference', 'succeeded', 'correlation-1', NULL, 'Summary', '{}');
            """);

        var update = await Assert.ThrowsExactlyAsync<SqliteException>(
            () => ExecuteAsync(connectionString, "UPDATE audit_events SET outcome = 'failed' WHERE id = 'audit-1';"));
        StringAssert.Contains(update.Message, "append-only", StringComparison.Ordinal);

        var delete = await Assert.ThrowsExactlyAsync<SqliteException>(
            () => ExecuteAsync(connectionString, "DELETE FROM audit_events WHERE id = 'audit-1';"));
        StringAssert.Contains(delete.Message, "append-only", StringComparison.Ordinal);

        Assert.AreEqual(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM audit_events;"));
    }

    [TestMethod]
    public async Task UnknownSchemaIsRejectedRatherThanGuessed()
    {
        var connectionString = this.CreateDatabasePath();
        await ExecuteAsync(connectionString, "CREATE TABLE unrelated (id TEXT NOT NULL PRIMARY KEY);");

        var failure = await Assert.ThrowsExactlyAsync<SqliteSchemaException>(
            () => SqliteSchemaMigrationRunner.MigrateAsync(connectionString));

        Assert.AreEqual(SqliteSchemaException.UnsupportedSchema, failure.Code);
    }

    [TestMethod]
    public async Task PartiallyUpgradedSchemaIsRejected()
    {
        var connectionString = this.CreateDatabasePath();
        await ExecuteAsync(connectionString, SqliteSchemaCatalog.Baseline.Sql);
        await ExecuteAsync(connectionString, "DROP TABLE widget_placements;");

        var failure = await Assert.ThrowsExactlyAsync<SqliteSchemaException>(
            () => SqliteSchemaMigrationRunner.MigrateAsync(connectionString));

        Assert.AreEqual(SqliteSchemaException.UnsupportedSchema, failure.Code);
        StringAssert.Contains(failure.Message, "widget_placements", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task RecordedChecksumMismatchFailsAndLeavesTheDatabaseUsable()
    {
        var connectionString = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        await ExecuteAsync(
            connectionString,
            $"""UPDATE "{SqliteSchemaHistory.TableName}" SET "checksum" = 'tampered' WHERE "migration_id" = '{SqliteSchemaCatalog.Baseline.Id}';""");

        var failure = await Assert.ThrowsExactlyAsync<SqliteSchemaException>(
            () => SqliteSchemaMigrationRunner.MigrateAsync(connectionString));

        Assert.AreEqual(SqliteSchemaException.ChecksumMismatch, failure.Code);
        Assert.AreEqual(
            1L,
            await ScalarAsync(connectionString, """SELECT count(*) FROM sqlite_master WHERE name = 'desktop_layouts';"""),
            "A rejected migration must leave the existing schema intact.");
    }

    [TestMethod]
    public async Task DatabaseFromANewerReleaseIsNotDowngraded()
    {
        var connectionString = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        await ExecuteAsync(
            connectionString,
            $"""
            INSERT INTO "{SqliteSchemaHistory.TableName}" ("migration_id", "checksum", "applied_at_utc")
            VALUES ('9999_from_the_future', 'unknown', '2030-01-01T00:00:00.0000000Z');
            """);

        var failure = await Assert.ThrowsExactlyAsync<SqliteSchemaException>(
            () => SqliteSchemaMigrationRunner.MigrateAsync(connectionString));

        Assert.AreEqual(SqliteSchemaException.UnsupportedSchema, failure.Code);
    }

    [TestMethod]
    public async Task HistoryRecordsEveryAppliedMigrationWithItsShippedChecksum()
    {
        var connectionString = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(TestContext.CancellationTokenSource.Token);
        var applied = await SqliteSchemaHistory.ReadAsync(connection, TestContext.CancellationTokenSource.Token);

        CollectionAssert.AreEqual(
            SqliteSchemaCatalog.Migrations.Select(migration => migration.Checksum).ToArray(),
            applied.Select(entry => entry.Checksum).ToArray());
    }

    [TestMethod]
    public async Task MigratedDatabaseMatchesTheCurrentEntityFrameworkModel()
    {
        var connectionString = this.CreateDatabasePath();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        var migrated = await ReadSchemaAsync(connectionString);

        var modelled = this.CreateDatabasePath();
        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CorePersistenceServiceCollectionExtensions.Configure(
            options,
            new CoreDatabaseConfiguration(CoreDatabaseProvider.Sqlite, modelled));
        await using (var context = new CoreDbContext(options.Options))
        {
            _ = await context.Database.EnsureCreatedAsync(TestContext.CancellationTokenSource.Token);
        }

        Assert.AreEqual(
            await ReadSchemaAsync(modelled),
            migrated,
            "The migration scripts and the Core model must not drift apart. Regenerate the scripts when the model changes.");
    }

    /// <summary>Test context supplied by the test platform.</summary>
    public TestContext TestContext { get; set; } = null!;

    private string CreateDatabasePath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-sqlite-migrations",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);
        return $"Data Source={Path.Combine(directory, "julos.db")}";
    }

    /// <summary>Copies a committed fixture database into a writable temporary location.</summary>
    private string CopyFixture(string fileName)
    {
        var source = Path.Combine(RepositoryRoot(), "tests", "fixtures", "sqlite", fileName);
        Assert.IsTrue(File.Exists(source), $"Fixture '{source}' is missing.");

        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-sqlite-migrations",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);

        var destination = Path.Combine(directory, fileName);
        File.Copy(source, destination);
        return $"Data Source={destination}";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "The repository root could not be located from the test output directory.");
        return directory.FullName;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    /// <summary>Reads the complete JulOS-owned schema as comparable normalized text.</summary>
    /// <remarks>
    /// Triggers are excluded because Entity Framework models none on either provider: both
    /// the PostgreSQL and the SQLite append-only audit guard are raw migration SQL.
    /// <see cref="MigratedDatabaseEnforcesAppendOnlyAuditEvents"/> covers them instead.
    /// </remarks>
    private static async Task<string> ReadSchemaAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var objects = await SqliteSchemaFingerprint.ReadAsync(connection, CancellationToken.None);
        return string.Join(
            "\n",
            objects
                .Where(entry => entry.Name != SqliteSchemaHistory.TableName)
                .Where(entry => entry.Type != "trigger")
                .Select(entry => $"{entry.Type} {entry.Name} {entry.Sql}"));
    }
}
