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
            .Where(path => !IsBuildOutput(path))
            .Select(RelativeToRoot)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns the committed project files below <paramref name="rootName"/>, for example <c>packages</c>.</summary>
    internal static IReadOnlyList<string> ProjectFilesUnder(string rootName)
    {
        return ProjectFiles()
            .Where(path => path.StartsWith(rootName + '/', StringComparison.Ordinal))
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
        var projectDirectory = DirectoryOf(projectFile);

        return Load(projectFile)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, Normalize(include!))))
            .Select(RelativeToRoot)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns the MSBuild SDK the project is built with, for example <c>Microsoft.NET.Sdk.Web</c>.</summary>
    internal static string SdkAttribute(string projectFile)
    {
        return Load(projectFile).Root?.Attribute("Sdk")?.Value ?? string.Empty;
    }

    /// <summary>Returns the shared framework references declared by the project.</summary>
    internal static IReadOnlyList<string> FrameworkReferences(string projectFile)
    {
        return Load(projectFile)
            .Descendants("FrameworkReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns the assembly name produced by the project.</summary>
    internal static string AssemblyName(string projectFile)
    {
        return Load(projectFile).Descendants("AssemblyName").FirstOrDefault()?.Value
            ?? Path.GetFileNameWithoutExtension(projectFile);
    }

    /// <summary>Returns the absolute directory containing <paramref name="projectFile"/>.</summary>
    internal static string DirectoryOf(string projectFile)
    {
        var absoluteProjectFile = Path.Combine(Root, Normalize(projectFile));

        return Path.GetDirectoryName(absoluteProjectFile)
            ?? throw new InvalidOperationException($"'{projectFile}' has no containing directory.");
    }

    private static XDocument Load(string projectFile)
    {
        return XDocument.Load(Path.Combine(Root, Normalize(projectFile)));
    }

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;

        return path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
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
