namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

/// <summary>One ordered SQLite core-schema migration.</summary>
/// <param name="Id">Ordered identifier, also the history-table key.</param>
/// <param name="Sql">Complete script text.</param>
/// <param name="Checksum">Lowercase hex SHA-256 over the normalized script.</param>
public sealed record SqliteSchemaMigration(string Id, string Sql, string Checksum);

/// <summary>The ordered SQLite core-schema migrations shipped with this build.</summary>
/// <remarks>
/// Scripts are embedded resources so a deployed Server carries exactly the migrations it
/// was built with. Order is the resource name order, which is why every identifier starts
/// with a zero-padded sequence number. An applied script is immutable: a model change adds
/// a new script instead of editing an existing one, which is what makes the recorded
/// checksums meaningful.
/// </remarks>
public static class SqliteSchemaCatalog
{
    private const string ResourcePrefix =
        "JulOS.Infrastructure.Persistence.Core.Sqlite.Scripts.";

    private static readonly Lazy<IReadOnlyList<SqliteSchemaMigration>> LazyMigrations =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Every migration, in application order.</summary>
    public static IReadOnlyList<SqliteSchemaMigration> Migrations => LazyMigrations.Value;

    /// <summary>
    /// The first migration, describing the schema every JulOS release created through
    /// <c>EnsureCreated</c>. An existing database whose fingerprint matches it is baselined
    /// here rather than recreated.
    /// </summary>
    public static SqliteSchemaMigration Baseline => Migrations[0];

    /// <summary>Computes the checksum of a script.</summary>
    /// <param name="sql">Script text.</param>
    /// <returns>Lowercase hex SHA-256 over the normalized text.</returns>
    /// <remarks>
    /// The text is normalized first so that a checked-out line ending or a trailing newline
    /// cannot invalidate an already-applied migration.
    /// </remarks>
    public static string ComputeChecksum(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var normalized = SqliteSchemaText.Normalize(sql);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static SqliteSchemaMigration[] Load()
    {
        var assembly = typeof(SqliteSchemaCatalog).Assembly;
        var names = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (names.Length == 0)
        {
            throw new InvalidOperationException(
                "No SQLite core-schema migration scripts are embedded in JulOS.Infrastructure.");
        }

        return names.Select(name => Read(assembly, name)).ToArray();
    }

    private static SqliteSchemaMigration Read(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Embedded migration script '{resourceName}' could not be opened."));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sql = reader.ReadToEnd();

        var id = Path.GetFileNameWithoutExtension(
            resourceName[ResourcePrefix.Length..]);

        return new SqliteSchemaMigration(id, sql, ComputeChecksum(sql));
    }
}
