namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

/// <summary>A SQLite core-schema migration could not be completed.</summary>
/// <remarks>
/// Every instance carries a stable <see cref="Code"/> so operators and the
/// <c>--migrate-database</c> exit status describe the same condition. The migration
/// never guesses at or repairs an unrecognised schema; it stops and reports.
/// </remarks>
public sealed class SqliteSchemaException : Exception
{
    /// <summary>The database holds a schema this release cannot recognise or upgrade.</summary>
    public const string UnsupportedSchema = "database.sqlite_schema_unsupported";

    /// <summary>An applied migration no longer matches the script shipped in this build.</summary>
    public const string ChecksumMismatch = "database.sqlite_schema_checksum_mismatch";

    /// <summary>A migration script failed while being applied.</summary>
    public const string MigrationFailed = "database.sqlite_schema_migration_failed";

    /// <summary>Creates a schema failure.</summary>
    /// <param name="code">Stable failure code.</param>
    /// <param name="message">Operator-facing explanation.</param>
    /// <param name="innerException">Underlying cause, when one exists.</param>
    public SqliteSchemaException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        this.Code = code;
    }

    /// <summary>Stable failure code.</summary>
    public string Code { get; }
}
