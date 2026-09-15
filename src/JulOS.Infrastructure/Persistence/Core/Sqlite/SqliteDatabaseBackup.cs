namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using System.Globalization;
using System.Security.Cryptography;

using Microsoft.Data.Sqlite;

/// <summary>Result of a SQLite core-database backup.</summary>
/// <param name="Path">Absolute path of the published backup file.</param>
/// <param name="SizeBytes">Size of the published file.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the published file.</param>
public sealed record SqliteBackupResult(string Path, long SizeBytes, string Sha256);

/// <summary>
/// Creates and restores consistent copies of a SQLite core database using SQLite's own
/// online backup API.
/// </summary>
/// <remarks>
/// <para>
/// Decision D043: the backup runs inside the Server process through
/// <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/> rather than through a
/// <c>sqlite3</c> command line. The runtime image therefore gains no extra tool, and the
/// copy is taken through the same library that holds the write-ahead log, so a concurrent
/// writer cannot produce a torn file.
/// </para>
/// <para>
/// A backup is written to a staging file, verified with <c>integrity_check</c>, and only
/// then published to its final name. A failed run leaves the staging file behind and never
/// touches the source database or a previously published backup.
/// </para>
/// </remarks>
public static class SqliteDatabaseBackup
{
    /// <summary>Creates a verified backup of a SQLite core database.</summary>
    /// <param name="connectionString">Connection string of the source database.</param>
    /// <param name="destinationPath">Path the verified backup is published to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The published backup.</returns>
    /// <exception cref="SqliteSchemaException">The copy failed its integrity check.</exception>
    public static async Task<SqliteBackupResult> CreateAsync(
        string connectionString,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var staging = destination + ".partial";
        DeleteDatabaseFiles(staging);

        await using (var source = new SqliteConnection(connectionString))
        {
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Fold the write-ahead log into the main database so the copy is complete
            // even when readers are active.
            await using (var checkpoint = source.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(FULL);";
                _ = await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var target = new SqliteConnection("Data Source=" + staging);
            await target.OpenAsync(cancellationToken).ConfigureAwait(false);
            source.BackupDatabase(target);
        }

        SqliteConnection.ClearAllPools();
        await VerifyAsync(staging, cancellationToken).ConfigureAwait(false);

        File.Move(staging, destination, overwrite: true);
        DeleteDatabaseFiles(staging);

        var bytes = await File.ReadAllBytesAsync(destination, cancellationToken).ConfigureAwait(false);
        return new SqliteBackupResult(
            destination,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>Verifies a backup and replaces the live database file with it.</summary>
    /// <param name="backupPath">Verified backup to restore from.</param>
    /// <param name="connectionString">Connection string of the database to replace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="SqliteSchemaException">The backup failed its integrity check.</exception>
    /// <remarks>
    /// The backup is checked before anything is replaced, so a corrupt archive cannot
    /// destroy a working database. The write-ahead log and shared-memory side files of the
    /// old database are removed with it; leaving them would apply stale pages on top of
    /// the restored file.
    /// </remarks>
    public static async Task RestoreAsync(
        string backupPath,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var backup = Path.GetFullPath(backupPath);
        if (!File.Exists(backup))
        {
            throw new SqliteSchemaException(
                SqliteSchemaException.MigrationFailed,
                string.Create(CultureInfo.InvariantCulture, $"Backup '{backup}' does not exist."));
        }

        await VerifyAsync(backup, cancellationToken).ConfigureAwait(false);

        var target = ResolveDatabasePath(connectionString);
        SqliteConnection.ClearAllPools();

        var staging = target + ".restoring";
        DeleteDatabaseFiles(staging);
        File.Copy(backup, staging, overwrite: true);

        await VerifyAsync(staging, cancellationToken).ConfigureAwait(false);

        DeleteDatabaseFiles(target);
        File.Move(staging, target, overwrite: true);
    }

    /// <summary>Resolves the database file a SQLite connection string points at.</summary>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <returns>The absolute database file path.</returns>
    public static string ResolveDatabasePath(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new SqliteSchemaException(
                SqliteSchemaException.MigrationFailed,
                "The configured SQLite core database is not a file and cannot be backed up or restored.");
        }

        return Path.GetFullPath(dataSource);
    }

    private static async Task VerifyAsync(string path, CancellationToken cancellationToken)
    {
        string? result;
        await using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            result = (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString();
        }

        SqliteConnection.ClearAllPools();

        if (!string.Equals(result, "ok", StringComparison.Ordinal))
        {
            throw new SqliteSchemaException(
                SqliteSchemaException.MigrationFailed,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"SQLite integrity check failed for '{path}': {result ?? "no result"}."));
        }
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}
