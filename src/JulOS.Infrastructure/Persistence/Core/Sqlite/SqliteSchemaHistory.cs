namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using Microsoft.Data.Sqlite;

/// <summary>A migration this database has already applied.</summary>
/// <param name="Id">Migration identifier.</param>
/// <param name="Checksum">Checksum recorded when it was applied.</param>
public sealed record SqliteAppliedMigration(string Id, string Checksum);

/// <summary>Reads and writes the SQLite core-schema history table.</summary>
/// <remarks>
/// The history table is JulOS-owned and deliberately separate from Entity Framework's
/// <c>__ef_migrations_history</c>, which stays the PostgreSQL record. Its presence is also
/// how the runner tells a database it already manages from an older <c>EnsureCreated</c>
/// database that still has to be baselined.
/// </remarks>
internal static class SqliteSchemaHistory
{
    /// <summary>Name of the JulOS-owned SQLite schema-history table.</summary>
    public const string TableName = "__julos_schema_history";

    /// <summary>Creates the history table when it does not exist yet.</summary>
    /// <param name="connection">Open SQLite connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task EnsureAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS "{TableName}" (
                "migration_id" TEXT NOT NULL CONSTRAINT "pk_julos_schema_history" PRIMARY KEY,
                "checksum" TEXT NOT NULL,
                "applied_at_utc" TEXT NOT NULL
            );
            """;
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reports whether the history table exists.</summary>
    /// <param name="connection">Open SQLite connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when this database is already runner-managed.</returns>
    public static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;";
        _ = command.Parameters.AddWithValue("$name", TableName);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    /// <summary>Reads the applied migrations in application order.</summary>
    /// <param name="connection">Open SQLite connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The recorded migrations.</returns>
    public static async Task<IReadOnlyList<SqliteAppliedMigration>> ReadAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var applied = new List<SqliteAppliedMigration>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""SELECT "migration_id", "checksum" FROM "{TableName}" ORDER BY "migration_id";""";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            applied.Add(new SqliteAppliedMigration(reader.GetString(0), reader.GetString(1)));
        }

        return applied;
    }

    /// <summary>Records a migration as applied.</summary>
    /// <param name="connection">Open SQLite connection.</param>
    /// <param name="transaction">Transaction the migration ran in.</param>
    /// <param name="migration">The applied migration.</param>
    /// <param name="appliedAtUtc">Time the migration completed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task RecordAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        SqliteSchemaMigration migration,
        DateTimeOffset appliedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(migration);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT INTO "{TableName}" ("migration_id", "checksum", "applied_at_utc")
            VALUES ($id, $checksum, $appliedAt);
            """;
        _ = command.Parameters.AddWithValue("$id", migration.Id);
        _ = command.Parameters.AddWithValue("$checksum", migration.Checksum);
        _ = command.Parameters.AddWithValue("$appliedAt", appliedAtUtc.UtcDateTime.ToString("O"));
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
