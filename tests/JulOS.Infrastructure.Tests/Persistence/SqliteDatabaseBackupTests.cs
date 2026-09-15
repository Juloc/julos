using JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.Data.Sqlite;

namespace JulOS.Infrastructure.Tests.Persistence;

/// <summary>
/// DB-001 / decision D043: the provider-aware SQLite backup path. The drill asserts that a
/// backup reproduces the database, that a corrupt archive is refused before anything is
/// replaced, and that a failed restore leaves the live database untouched.
/// </summary>
[TestClass]
public sealed class SqliteDatabaseBackupTests
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
                // Leftover temporary files are not a test failure.
            }
        }
    }

    [TestMethod]
    public async Task BackupReproducesTheDatabaseAndLeavesTheSourceUnchanged()
    {
        var (connectionString, databasePath) = this.CreateDatabase();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        await SeedAsync(connectionString, "layout-1");

        // Release the pooled handle so the test can read the file the same way an
        // operator inspecting the volume would.
        SqliteConnection.ClearAllPools();
        var sourceBefore = await File.ReadAllBytesAsync(databasePath);

        var destination = Path.Combine(Path.GetDirectoryName(databasePath)!, "backup", "core.db");
        var result = await SqliteDatabaseBackup.CreateAsync(connectionString, destination);

        Assert.IsTrue(File.Exists(result.Path));
        Assert.AreEqual(64, result.Sha256.Length);
        Assert.IsFalse(File.Exists(result.Path + ".partial"), "The staging file is removed after publication.");

        var restoredConnection = $"Data Source={result.Path}";
        Assert.AreEqual(1L, await ScalarAsync(restoredConnection, "SELECT count(*) FROM desktop_layouts WHERE id = 'layout-1';"));

        SqliteConnection.ClearAllPools();
        CollectionAssert.AreEqual(
            sourceBefore,
            await File.ReadAllBytesAsync(databasePath),
            "A backup must not modify the source database.");
    }

    [TestMethod]
    public async Task RestoreReplacesTheLiveDatabase()
    {
        var (connectionString, databasePath) = this.CreateDatabase();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        await SeedAsync(connectionString, "layout-1");

        var destination = Path.Combine(Path.GetDirectoryName(databasePath)!, "backup", "core.db");
        _ = await SqliteDatabaseBackup.CreateAsync(connectionString, destination);

        await SeedAsync(connectionString, "layout-2");
        Assert.AreEqual(2L, await ScalarAsync(connectionString, "SELECT count(*) FROM desktop_layouts;"));

        await SqliteDatabaseBackup.RestoreAsync(destination, connectionString);

        Assert.AreEqual(
            1L,
            await ScalarAsync(connectionString, "SELECT count(*) FROM desktop_layouts;"),
            "The restored database must contain exactly what the backup contained.");
        Assert.IsFalse(File.Exists(databasePath + ".restoring"));
    }

    [TestMethod]
    public async Task CorruptBackupIsRefusedAndLeavesTheLiveDatabaseUsable()
    {
        var (connectionString, databasePath) = this.CreateDatabase();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);
        await SeedAsync(connectionString, "layout-1");

        var destination = Path.Combine(Path.GetDirectoryName(databasePath)!, "backup", "core.db");
        _ = await SqliteDatabaseBackup.CreateAsync(connectionString, destination);

        // Injected failure: damage the archive's page data, keeping a valid SQLite header
        // so the corruption is only found by the integrity check.
        var archive = await File.ReadAllBytesAsync(destination);
        for (var index = 4096; index < Math.Min(archive.Length, 8192); index++)
        {
            archive[index] = 0xFF;
        }

        await File.WriteAllBytesAsync(destination, archive);
        SqliteConnection.ClearAllPools();

        _ = await Assert.ThrowsExactlyAsync<SqliteSchemaException>(
            () => SqliteDatabaseBackup.RestoreAsync(destination, connectionString));

        Assert.AreEqual(
            1L,
            await ScalarAsync(connectionString, "SELECT count(*) FROM desktop_layouts;"),
            "A refused restore must leave the live database untouched.");
    }

    [TestMethod]
    public async Task MissingBackupIsReportedRatherThanIgnored()
    {
        var (connectionString, databasePath) = this.CreateDatabase();
        _ = await SqliteSchemaMigrationRunner.MigrateAsync(connectionString);

        var missing = Path.Combine(Path.GetDirectoryName(databasePath)!, "absent.db");

        var failure = await Assert.ThrowsExactlyAsync<SqliteSchemaException>(
            () => SqliteDatabaseBackup.RestoreAsync(missing, connectionString));

        StringAssert.Contains(failure.Message, "does not exist", StringComparison.Ordinal);
    }

    [TestMethod]
    public void InMemoryDatabaseCannotBeBackedUp()
    {
        var failure = Assert.ThrowsExactly<SqliteSchemaException>(
            () => SqliteDatabaseBackup.ResolveDatabasePath("Data Source=:memory:"));

        StringAssert.Contains(failure.Message, "not a file", StringComparison.Ordinal);
    }

    private (string ConnectionString, string DatabasePath) CreateDatabase()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "julos-sqlite-backup",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        this.directories.Add(directory);

        var path = Path.Combine(directory, "julos.db");
        return ($"Data Source={path}", path);
    }

    private static async Task SeedAsync(string connectionString, string layoutId)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO desktop_layouts (id, user_id, workspace_class, client_device_id, name,
                                        presentation_mode, display_count, revision, updated_at_utc)
            VALUES ($id, 'user-1', $class, NULL, $id, 'Freeform', 1, 1, '2026-01-01 00:00:00');
            """;
        _ = command.Parameters.AddWithValue("$id", layoutId);
        // One shared layout per workspace class, so two seeded layouts need two classes.
        _ = command.Parameters.AddWithValue(
            "$class",
            string.Equals(layoutId, "layout-1", StringComparison.Ordinal) ? "DesktopSingle" : "Tablet");
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
}
