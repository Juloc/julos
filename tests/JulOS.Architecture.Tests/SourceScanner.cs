using System.Text.RegularExpressions;

namespace JulOS.Architecture.Tests;

/// <summary>A term found in a source file, reported with enough context to locate it.</summary>
/// <param name="File">Path relative to the repository root.</param>
/// <param name="Line">One-based line number.</param>
/// <param name="Term">The matched text.</param>
internal sealed record SourceMatch(string File, int Line, string Term)
{
    public override string ToString() => $"{File}:{Line} ({Term})";
}

/// <summary>Searches committed C# sources for forbidden terminology.</summary>
internal static class SourceScanner
{
    /// <summary>Returns every committed C# file of a project, excluding generated build output.</summary>
    internal static IReadOnlyList<string> SourceFiles(string projectFile)
    {
        var projectDirectory = Repository.DirectoryOf(projectFile);
        var separator = Path.DirectorySeparatorChar;

        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Returns every occurrence of <paramref name="terms"/> in the project's sources.
    /// Comments are included on purpose: a boundary document states that a layer does not
    /// know a product, so naming that product in the layer is a violation worth reviewing.
    /// </summary>
    internal static IReadOnlyList<SourceMatch> Find(string projectFile, IReadOnlyCollection<string> terms)
    {
        var pattern = IdentifierPattern(terms);
        var matches = new List<SourceMatch>();

        foreach (var file in SourceFiles(projectFile))
        {
            var lines = File.ReadAllLines(file);
            var relativeFile = Path.GetRelativePath(Repository.Root, file).Replace('\\', '/');

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (Match match in pattern.Matches(lines[index]))
                {
                    matches.Add(new SourceMatch(relativeFile, index + 1, match.Value));
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Builds the matcher used by <see cref="Find"/>. A term matches when it forms a complete
    /// word or a complete PascalCase segment of an identifier, so <c>DockerClient</c> and
    /// <c>useDockerHost</c> are found while <c>hardPath</c> and <c>PressHandler</c> are not.
    /// </summary>
    /// <param name="terms">Terms written in PascalCase, for example <c>WebDav</c>.</param>
    internal static Regex IdentifierPattern(IReadOnlyCollection<string> terms)
    {
        var alternatives = terms
            .Select(Regex.Escape)
            .SelectMany(term => new[]
            {
                // A standalone word, in any casing.
                $"(?<![A-Za-z0-9])(?i:{term})(?![a-z0-9])",

                // A PascalCase segment inside a longer identifier.
                $"(?<=[a-z0-9]){term}(?![a-z0-9])",
            });

        return new Regex(
            string.Join('|', alternatives),
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
    }
}
