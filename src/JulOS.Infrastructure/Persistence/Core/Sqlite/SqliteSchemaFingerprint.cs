namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;

/// <summary>One schema object as SQLite records it.</summary>
/// <param name="Type">Object type, for example <c>table</c> or <c>index</c>.</param>
/// <param name="Name">Object name.</param>
/// <param name="Sql">Normalized <c>CREATE</c> statement.</param>
public sealed record SqliteSchemaObject(string Type, string Name, string Sql);

/// <summary>
/// Reads the schema objects an existing SQLite database actually contains and compares
/// them against a migration script.
/// </summary>
/// <remarks>
/// A field database is only baselined when its complete object set matches the baseline
/// script exactly. A partially upgraded, hand-edited or foreign database therefore fails
/// with <see cref="SqliteSchemaException.UnsupportedSchema"/> instead of being guessed at,
/// which is the behaviour <c>docs/WORK_BREAKDOWN.md</c> requires for DB-001.
/// </remarks>
internal static partial class SqliteSchemaFingerprint
{
    /// <summary>Reads every JulOS-owned schema object from a database.</summary>
    /// <param name="connection">Open SQLite connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The objects, ordered by type and name.</returns>
    /// <remarks>
    /// SQLite's own bookkeeping objects (<c>sqlite_sequence</c>, auto-indexes) are excluded:
    /// SQLite creates and drops them itself, so they are not part of what a migration owns.
    /// Auto-indexes are recognised by carrying no <c>CREATE</c> statement.
    /// </remarks>
    public static async Task<IReadOnlyList<SqliteSchemaObject>> ReadAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var objects = new List<SqliteSchemaObject>();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT type, name, sql
            FROM sqlite_master
            WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%'
            ORDER BY type, name;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            objects.Add(new SqliteSchemaObject(
                reader.GetString(0),
                reader.GetString(1),
                SqliteSchemaText.Normalize(reader.GetString(2))));
        }

        return objects;
    }

    /// <summary>Extracts the schema objects a migration script creates.</summary>
    /// <param name="sql">Migration script text.</param>
    /// <returns>The objects, ordered by type and name.</returns>
    public static IReadOnlyList<SqliteSchemaObject> ReadFromScript(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var objects = new List<SqliteSchemaObject>();

        foreach (var statement in SqliteSchemaScript.SplitStatements(sql))
        {
            var match = CreateStatement().Match(statement);
            if (!match.Success)
            {
                continue;
            }

            objects.Add(new SqliteSchemaObject(
                match.Groups["type"].Value.ToLowerInvariant(),
                match.Groups["name"].Value,
                SqliteSchemaText.Normalize(statement)));
        }

        return objects
            .OrderBy(entry => entry.Type, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Describes how an observed schema differs from an expected one.</summary>
    /// <param name="expected">Objects the baseline script creates.</param>
    /// <param name="observed">Objects the database actually contains.</param>
    /// <returns><see langword="null"/> when they match, otherwise an operator-facing difference.</returns>
    public static string? Compare(
        IReadOnlyList<SqliteSchemaObject> expected,
        IReadOnlyList<SqliteSchemaObject> observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);

        var expectedByKey = expected.ToDictionary(Key, StringComparer.Ordinal);
        var observedByKey = observed.ToDictionary(Key, StringComparer.Ordinal);

        var missing = expectedByKey.Keys.Except(observedByKey.Keys, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var unexpected = observedByKey.Keys.Except(expectedByKey.Keys, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var changed = expectedByKey.Keys.Intersect(observedByKey.Keys, StringComparer.Ordinal)
            .Where(key => !string.Equals(expectedByKey[key].Sql, observedByKey[key].Sql, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length == 0 && unexpected.Length == 0 && changed.Length == 0)
        {
            return null;
        }

        var differences = new List<string>();
        Describe(differences, "missing", missing);
        Describe(differences, "unexpected", unexpected);
        Describe(differences, "different", changed);
        return string.Join("; ", differences);
    }

    private static void Describe(List<string> differences, string label, string[] keys)
    {
        const int Shown = 5;
        if (keys.Length == 0)
        {
            return;
        }

        var names = string.Join(", ", keys.Take(Shown));
        var suffix = keys.Length > Shown
            ? string.Create(CultureInfo.InvariantCulture, $" and {keys.Length - Shown} more")
            : string.Empty;
        differences.Add(string.Create(CultureInfo.InvariantCulture, $"{label}: {names}{suffix}"));
    }

    private static string Key(SqliteSchemaObject entry) => entry.Type + " " + entry.Name;

    // sqlite_master records a unique index with type 'index', so UNIQUE is matched
    // outside the captured type.
    [GeneratedRegex(
        "^CREATE\\s+(?:UNIQUE\\s+)?(?<type>TABLE|INDEX|TRIGGER|VIEW)\\s+\"(?<name>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateStatement();
}
