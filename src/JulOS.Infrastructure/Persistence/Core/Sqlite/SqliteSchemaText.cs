namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using System.Text;

/// <summary>Whitespace normalization shared by checksums and schema fingerprints.</summary>
/// <remarks>
/// SQLite stores the <c>CREATE</c> statement it was given verbatim, so comparing a live
/// database against a shipped script means comparing SQL text. Only layout may differ
/// between them — a checked-out line ending, or the indentation Entity Framework happened
/// to emit — so every run of whitespace collapses to a single space before comparison.
/// Anything that survives this normalization is a real schema difference.
/// </remarks>
internal static class SqliteSchemaText
{
    /// <summary>Collapses every whitespace run to a single space and trims.</summary>
    /// <param name="value">Raw SQL text.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                _ = builder.Append(' ');
                pendingSpace = false;
            }

            _ = builder.Append(character);
        }

        return builder.ToString();
    }
}
