namespace JulOS.Architecture.Tests;

/// <summary>Verifies the dependency direction defined in <c>docs/ARCHITECTURE.md</c> section 2.</summary>
[TestClass]
public sealed class ProjectReferenceTests
{
    /// <summary>
    /// The complete allowed project dependency graph. A project that is not listed fails
    /// the coverage test, so adding a project forces an explicit boundary decision.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        [PlatformProjects.Domain] = [],
        [PlatformProjects.Contracts] = [],
        [PlatformProjects.Application] = [PlatformProjects.Domain, PlatformProjects.Contracts],
        [PlatformProjects.Infrastructure] = [PlatformProjects.Application],
        [PlatformProjects.Server] = [PlatformProjects.Application, PlatformProjects.Infrastructure],
        [PlatformProjects.PackageSdk] = [PlatformProjects.Contracts],
        [PlatformProjects.Agent] = [PlatformProjects.Contracts],
        [PlatformProjects.RuntimeManager] = [PlatformProjects.Contracts],
        [PlatformProjects.ArchitectureTests] = [],
    };

    [TestMethod]
    public void AllowedGraphCoversEveryPlatformProject()
    {
        var committed = PlatformProjectFiles();
        var declared = AllowedReferences.Keys.Order(StringComparer.Ordinal).ToArray();

        var undeclared = committed.Except(declared, StringComparer.Ordinal).ToArray();
        var stale = declared.Except(committed, StringComparer.Ordinal).ToArray();

        Assert.AreEqual(
            0,
            undeclared.Length,
            $"These projects have no declared allowed dependencies: {string.Join(", ", undeclared)}.");

        Assert.AreEqual(
            0,
            stale.Length,
            $"These projects have declared allowed dependencies but no longer exist: {string.Join(", ", stale)}.");
    }

    [TestMethod]
    public void PlatformProjectsReferenceOnlyAllowedProjects()
    {
        var violations = new List<string>();

        foreach (var projectFile in PlatformProjectFiles())
        {
            if (!AllowedReferences.TryGetValue(projectFile, out var allowed))
            {
                continue;
            }

            violations.AddRange(Repository
                .ProjectReferences(projectFile)
                .Except(allowed, StringComparer.Ordinal)
                .Select(reference => $"{projectFile} -> {reference}"));
        }

        Assert.AreEqual(
            0,
            violations.Count,
            $"Forbidden project references: {string.Join("; ", violations)}.");
    }

    [TestMethod]
    public void PackageProjectsDoNotReferenceEachOther()
    {
        var packageProjects = Repository.ProjectFilesUnder(PlatformProjects.PackagesRoot);

        if (packageProjects.Count == 0)
        {
            Assert.Inconclusive(
                $"No project exists under '{PlatformProjects.PackagesRoot}/' yet, so this rule has nothing to check.");
        }

        var violations = new List<string>();

        foreach (var projectFile in packageProjects)
        {
            var ownPackage = PackageOf(projectFile);

            violations.AddRange(Repository
                .ProjectReferences(projectFile)
                .Where(reference => reference.StartsWith(PlatformProjects.PackagesRoot + '/', StringComparison.Ordinal))
                .Where(reference => !string.Equals(PackageOf(reference), ownPackage, StringComparison.Ordinal))
                .Select(reference => $"{projectFile} -> {reference}"));
        }

        Assert.AreEqual(
            0,
            violations.Count,
            $"Packages collaborate through brokered capabilities, never direct references: {string.Join("; ", violations)}.");
    }

    private static string[] PlatformProjectFiles()
    {
        return Repository
            .ProjectFiles()
            .Where(path => !path.StartsWith(PlatformProjects.PackagesRoot + '/', StringComparison.Ordinal))
            .ToArray();
    }

    private static string PackageOf(string projectFile)
    {
        return projectFile.Split('/')[1];
    }
}
