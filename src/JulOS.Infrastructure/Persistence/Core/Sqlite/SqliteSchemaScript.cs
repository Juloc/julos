namespace JulOS.Infrastructure.Persistence.Core.Sqlite;

using System.Text;

/// <summary>Splits a migration script into individual statements.</summary>
/// <remarks>
/// The migration scripts are generated from the Core model and contain only
/// <c>CREATE</c>, <c>INSERT</c>, <c>DROP</c> and <c>ALTER</c> statements terminated by a
/// semicolon. The splitter still tracks string literals and comments so that a semicolon
/// inside a <c>CHECK</c> expression or a comment cannot cut a statement in half.
/// </remarks>
internal static class SqliteSchemaScript
{
    /// <summary>Splits a script into executable statements.</summary>
    /// <param name="sql">Script text.</param>
    /// <returns>Each non-empty statement, without its terminating semicolon.</returns>
    public static IReadOnlyList<string> SplitStatements(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var statements = new List<string>();
        var current = new StringBuilder();
        var inLiteral = false;
        var inLineComment = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];

            if (inLineComment)
            {
                if (character is '\n')
                {
                    inLineComment = false;
                    _ = current.Append(character);
                }

                continue;
            }

            if (inLiteral)
            {
                _ = current.Append(character);
                if (character is '\'')
                {
                    // '' inside a literal is an escaped quote, not the end of the literal.
                    if (index + 1 < sql.Length && sql[index + 1] is '\'')
                    {
                        _ = current.Append(sql[++index]);
                    }
                    else
                    {
                        inLiteral = false;
                    }
                }

                continue;
            }

            switch (character)
            {
                case '\'':
                    inLiteral = true;
                    _ = current.Append(character);
                    break;

                case '-' when index + 1 < sql.Length && sql[index + 1] is '-':
                    inLineComment = true;
                    index++;
                    break;

                // A CREATE TRIGGER body is a BEGIN ... END block containing its own
                // semicolons, so it only ends at the semicolon after END.
                case ';' when IsUnterminatedTriggerBody(current):
                    _ = current.Append(character);
                    break;

                case ';':
                    Flush(statements, current);
                    break;

                default:
                    _ = current.Append(character);
                    break;
            }
        }

        Flush(statements, current);
        return statements;
    }

    /// <summary>Reports whether the statement so far is a trigger whose body is still open.</summary>
    private static bool IsUnterminatedTriggerBody(StringBuilder current)
    {
        var statement = current.ToString().TrimStart();
        if (!statement.StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The body is closed once a standalone END has been written. Matching the word
        // avoids ending early on an identifier such as "appended".
        var trailing = statement.TrimEnd();
        return !trailing.EndsWith("END", StringComparison.OrdinalIgnoreCase)
            || (trailing.Length > 3 && char.IsLetterOrDigit(trailing[^4]));
    }

    private static void Flush(List<string> statements, StringBuilder current)
    {
        var statement = current.ToString().Trim();
        _ = current.Clear();

        if (statement.Length > 0)
        {
            statements.Add(statement);
        }
    }
}
