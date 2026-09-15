namespace JulOS.Infrastructure.Persistence.Core;

using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Provider-specific rewrites for the few Core check constraints whose PostgreSQL
/// expression uses a function SQLite does not provide.
/// </summary>
/// <remarks>
/// The Core model declares one check-constraint expression per rule, written for
/// PostgreSQL. Every expression is also valid SQLite except the ones listed here, so
/// SQLite keeps the identical rule under the identical constraint name and only the
/// function spelling differs. A constraint that is absent from this table is used
/// unchanged on both providers.
/// </remarks>
internal static class SqliteCheckConstraintTranslations
{
    /// <summary>
    /// SQLite spells the character-count function <c>length</c>; PostgreSQL uses
    /// <c>char_length</c>. For <c>TEXT</c> values the two are exactly equivalent.
    /// </summary>
    private static readonly Dictionary<string, string> Translations = new(StringComparer.Ordinal)
    {
        ["ck_agent_capabilities_metadata_length"] = "length(metadata) <= 8192",
    };

    /// <summary>Looks up the SQLite expression for a Core check constraint.</summary>
    /// <param name="name">Database constraint name declared by the Core model.</param>
    /// <param name="sql">The SQLite expression when one is declared.</param>
    /// <returns><see langword="true"/> when SQLite needs its own expression.</returns>
    public static bool TryTranslate(string name, [NotNullWhen(true)] out string? sql) =>
        Translations.TryGetValue(name, out sql);

    /// <summary>Constraint names that carry a SQLite-specific expression.</summary>
    /// <remarks>Tests assert that every declared name still exists in the Core model.</remarks>
    public static IReadOnlyCollection<string> TranslatedConstraintNames => Translations.Keys;
}
