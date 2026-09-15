using JulOS.Infrastructure.Persistence.Core;
using JulOS.Infrastructure.Persistence.Core.Sqlite;

namespace JulOS.Server;

/// <summary>The explicit core database migration process.</summary>
/// <remarks>
/// This is the only process that changes the core schema; normal Server startup never
/// does. The exit code distinguishes a successful run from a schema this build refuses to
/// touch, so a deployment stack can stop instead of starting Server against a database it
/// does not understand.
/// </remarks>
internal static class DatabaseMigrationCommand
{
    private const string CommandSwitch = "--migrate-database";

    /// <summary>Exit code for a schema this build cannot recognise or upgrade.</summary>
    internal const int UnsupportedSchema = 4;

    /// <summary>Exit code for a migration that failed and was rolled back.</summary>
    internal const int MigrationFailure = 5;

    internal static bool IsRequested(string[] arguments) =>
        Array.IndexOf(arguments, CommandSwitch) >= 0;

    internal static async Task<int> RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var database = CoreDatabaseConfiguration.Read(configuration);

        try
        {
            if (database.Provider == CoreDatabaseProvider.Sqlite)
            {
                var applied = await SqliteSchemaMigrationRunner
                    .MigrateAsync(database.ConnectionString, timeProvider: null, cancellationToken)
                    .ConfigureAwait(false);

                Console.WriteLine(applied.Count == 0
                    ? "The SQLite core database is already at the current schema."
                    : $"Applied {applied.Count} SQLite migration(s): {string.Join(", ", applied)}");
                return 0;
            }

            await CoreDatabaseMigrator
                .MigrateAsync(database, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine("The PostgreSQL core database is at the current schema.");
            return 0;
        }
        catch (SqliteSchemaException exception)
        {
            await Console.Error
                .WriteLineAsync($"{exception.Code}: {exception.Message}")
                .ConfigureAwait(false);

            return exception.Code switch
            {
                SqliteSchemaException.UnsupportedSchema => UnsupportedSchema,
                SqliteSchemaException.ChecksumMismatch => UnsupportedSchema,
                _ => MigrationFailure,
            };
        }
    }
}
