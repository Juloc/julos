namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using System.Globalization;

using Microsoft.Data.Sqlite;

/// <summary>Applies the ordered SQLite core-schema migrations.</summary>
/// <remarks>
/// <para>
/// This runner is the only supported way a SQLite core database reaches the current
/// schema, and it is invoked only by the explicit <c>--migrate-database</c> command.
/// Normal Server startup never changes the schema.
/// </para>
/// <para>
/// Three cases are distinguished, and nothing else is accepted:
/// an empty database gets every migration; a database this runner already manages gets
/// the pending ones; an existing database created by an older release is baselined only
/// when its complete schema fingerprint matches the baseline script. Anything else fails
/// with <see cref="SqliteSchemaException.UnsupportedSchema"/> rather than being repaired.
/// </para>
/// </remarks>
public static class SqliteSchemaMigrationRunner
{
    /// <summary>Brings a SQLite core database to the current schema.</summary>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <param name="timeProvider">Clock used for the applied-at record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The migrations applied by this run, in order.</returns>
    /// <exception cref="SqliteSchemaException">The schema is unsupported or a migration failed.</exception>
    public static async Task<IReadOnlyList<string>> MigrateAsync(
        string connectionString,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var clock = timeProvider ?? TimeProvider.System;

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Foreign keys are disabled for the whole run because migration 0002 rebuilds
        // tables other tables reference, and legacy_alter_table keeps the RENAME step of
        // that rebuild from rewriting those references. Both are the procedure the SQLite
        // manual documents for schema changes it cannot express as ALTER TABLE.
        await ExecuteAsync(connection, "PRAGMA foreign_keys=OFF;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA legacy_alter_table=ON;", cancellationToken).ConfigureAwait(false);

        try
        {
            var pending = await ResolvePendingAsync(connection, clock, cancellationToken).ConfigureAwait(false);

            var applied = new List<string>();
            foreach (var migration in pending)
            {
                await ApplyAsync(connection, migration, clock, cancellationToken).ConfigureAwait(false);
                applied.Add(migration.Id);
            }

            await VerifyForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
            return applied;
        }
        finally
        {
            await ExecuteAsync(connection, "PRAGMA legacy_alter_table=OFF;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<SqliteSchemaMigration>> ResolvePendingAsync(
        SqliteConnection connection,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var managed = await SqliteSchemaHistory.ExistsAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!managed)
        {
            var existing = await SqliteSchemaFingerprint.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
            if (existing.Count > 0)
            {
                await BaselineAsync(connection, existing, clock, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SqliteSchemaHistory.EnsureAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }

        var applied = await SqliteSchemaHistory.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
        VerifyChecksums(applied);

        var appliedIds = applied.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        return SqliteSchemaCatalog.Migrations
            .Where(migration => !appliedIds.Contains(migration.Id))
            .ToArray();
    }

    private static async Task BaselineAsync(
        SqliteConnection connection,
        IReadOnlyList<SqliteSchemaObject> existing,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var baseline = SqliteSchemaCatalog.Baseline;
        var expected = SqliteSchemaFingerprint.ReadFromScript(baseline.Sql);
        var difference = SqliteSchemaFingerprint.Compare(expected, existing);

        if (difference is not null)
        {
            throw new SqliteSchemaException(
                SqliteSchemaException.UnsupportedSchema,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The SQLite core database does not match the supported '{baseline.Id}' schema and cannot be upgraded automatically ({difference}). Restore a supported backup or start from an empty database."));
        }

        await SqliteSchemaHistory.EnsureAsync(connection, cancellationToken).ConfigureAwait(false);
        await SqliteSchemaHistory
            .RecordAsync(connection, transaction: null, baseline, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void VerifyChecksums(IReadOnlyList<SqliteAppliedMigration> applied)
    {
        var shipped = SqliteSchemaCatalog.Migrations.ToDictionary(
            migration => migration.Id,
            StringComparer.Ordinal);

        foreach (var entry in applied)
        {
            if (!shipped.TryGetValue(entry.Id, out var migration))
            {
                throw new SqliteSchemaException(
                    SqliteSchemaException.UnsupportedSchema,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The SQLite core database recorded migration '{entry.Id}', which this JulOS build does not contain. It was created by a newer release and must not be downgraded."));
            }

            if (!string.Equals(entry.Checksum, migration.Checksum, StringComparison.Ordinal))
            {
                throw new SqliteSchemaException(
                    SqliteSchemaException.ChecksumMismatch,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Migration '{entry.Id}' was applied with checksum {entry.Checksum} but this build ships {migration.Checksum}. An applied migration must never be edited."));
            }
        }
    }

    private static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteSchemaMigration migration,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Each migration owns its transaction, so an interrupted run leaves the database
        // usable at the last migration that completed rather than half-applied.
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            foreach (var statement in SqliteSchemaScript.SplitStatements(migration.Sql))
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = statement;
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await SqliteSchemaHistory
                .RecordAsync(connection, transaction, migration, clock.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

            // A constraint violation here means existing rows do not satisfy a rule this
            // release enforces. The migration reports that instead of deleting or
            // rewriting the offending rows.
            throw new SqliteSchemaException(
                SqliteSchemaException.MigrationFailed,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Migration '{migration.Id}' failed and was rolled back; the database remains at the previous migration. SQLite reported: {exception.Message}"),
                exception);
        }
    }

    private static async Task VerifyForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var table = reader.IsDBNull(0) ? "unknown" : reader.GetString(0);
        throw new SqliteSchemaException(
            SqliteSchemaException.MigrationFailed,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The SQLite core database has foreign-key violations after migration, starting in table '{table}'."));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
