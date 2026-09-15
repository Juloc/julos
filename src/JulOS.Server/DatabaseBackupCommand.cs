using System.Globalization;

using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Persistence.Core.Sqlite;

namespace JulOS.Server;

/// <summary>The explicit core-database backup and restore processes.</summary>
/// <remarks>
/// Decision D043 keeps these in the Server process so the runtime image needs no database
/// command-line tool. Both switches take the archive path as the following argument:
/// <c>--backup-database /backup/core.db</c> and <c>--restore-database /backup/core.db</c>.
/// Only the default SQLite store is handled here; PostgreSQL deployments keep using
/// <c>pg_dump</c>/<c>pg_restore</c> through <c>tools/backup.sh</c>.
/// </remarks>
internal static class DatabaseBackupCommand
{
    private const string BackupSwitch = "--backup-database";
    private const string RestoreSwitch = "--restore-database";

    /// <summary>Exit code for a missing or unusable argument.</summary>
    internal const int UsageFailure = 2;

    /// <summary>Exit code for a failed backup, restore or verification.</summary>
    internal const int OperationFailure = 3;

    internal static bool IsBackupRequested(string[] arguments) =>
        Array.IndexOf(arguments, BackupSwitch) >= 0;

    internal static bool IsRestoreRequested(string[] arguments) =>
        Array.IndexOf(arguments, RestoreSwitch) >= 0;

    internal static async Task<int> RunBackupAsync(
        string[] arguments,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!TryReadPath(arguments, BackupSwitch, out var path)
            || !TryReadSqliteConnection(configuration, out var connectionString))
        {
            return UsageFailure;
        }

        try
        {
            var result = await SqliteDatabaseBackup
                .CreateAsync(connectionString, path, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Wrote {result.SizeBytes} bytes to {result.Path}"));
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"sha256 {result.Sha256}"));
            return 0;
        }
        catch (SqliteSchemaException exception)
        {
            await WriteFailureAsync(exception).ConfigureAwait(false);
            return OperationFailure;
        }
    }

    internal static async Task<int> RunRestoreAsync(
        string[] arguments,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!TryReadPath(arguments, RestoreSwitch, out var path)
            || !TryReadSqliteConnection(configuration, out var connectionString))
        {
            return UsageFailure;
        }

        try
        {
            await SqliteDatabaseBackup
                .RestoreAsync(path, connectionString, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Restored {SqliteDatabaseBackup.ResolveDatabasePath(connectionString)} from {path}"));
            return 0;
        }
        catch (SqliteSchemaException exception)
        {
            await WriteFailureAsync(exception).ConfigureAwait(false);
            return OperationFailure;
        }
    }

    private static bool TryReadPath(string[] arguments, string commandSwitch, out string path)
    {
        var index = Array.IndexOf(arguments, commandSwitch);
        if (index >= 0 && index + 1 < arguments.Length && !string.IsNullOrWhiteSpace(arguments[index + 1]))
        {
            path = arguments[index + 1];
            return true;
        }

        Console.Error.WriteLine($"{commandSwitch} requires the archive path as the next argument.");
        path = string.Empty;
        return false;
    }

    private static bool TryReadSqliteConnection(IConfiguration configuration, out string connectionString)
    {
        var database = CoreDatabaseConfiguration.Read(configuration);
        if (database.Provider == CoreDatabaseProvider.Sqlite)
        {
            connectionString = database.ConnectionString;
            return true;
        }

        Console.Error.WriteLine(
            "The configured core database is PostgreSQL. Use tools/backup.sh, which runs pg_dump.");
        connectionString = string.Empty;
        return false;
    }

    private static async Task WriteFailureAsync(SqliteSchemaException exception) =>
        await Console.Error
            .WriteLineAsync($"{exception.Code}: {exception.Message}")
            .ConfigureAwait(false);
}
