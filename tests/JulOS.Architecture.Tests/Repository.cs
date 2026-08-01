using System.Xml.Linq;

namespace JulOS.Architecture.Tests;

/// <summary>
/// Reads the real repository layout so architecture rules are validated against
/// committed project files rather than against compiled test dependencies.
/// </summary>
internal static class Repository
{
    private const string SolutionFileName = "JulOS.slnx";

    private static readonly string[] SourceRootNames = ["src", "tests", "packages"];

    private static readonly Lazy<string> LazyRoot = new(FindRoot);

    internal static string Root => LazyRoot.Value;

    internal static string SolutionFile => Path.Combine(Root, SolutionFileName);

    /// <summary>Returns every committed project file, ordered and relative to the repository root.</summary>
    internal static IReadOnlyList<string> ProjectFiles()
    {
        return SourceRootNames
            .Select(name => Path.Combine(Root, name))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            .Select(RelativeToRoot)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns the project paths listed in the solution, relative to the repository root.</summary>
    internal static IReadOnlyList<string> SolutionProjectFiles()
    {
        return XDocument
            .Load(SolutionFile)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns the projects referenced by <paramref name="projectFile"/>, relative to the repository root.</summary>
    internal static IReadOnlyList<string> ProjectReferences(string projectFile)
    {
        var absoluteProjectFile = Path.Combine(Root, Normalize(projectFile));
        var projectDirectory = Path.GetDirectoryName(absoluteProjectFile)
            ?? throw new InvalidOperationException($"'{projectFile}' has no containing directory.");

        return XDocument
            .Load(absoluteProjectFile)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, Normalize(include!))))
            .Select(RelativeToRoot)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string RelativeToRoot(string absolutePath)
    {
        return Path.GetRelativePath(Root, absolutePath).Replace('\\', '/');
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"'{SolutionFileName}' was not found in any directory above '{AppContext.BaseDirectory}'.");
    }
}
