namespace JulOS.Architecture.Tests;

/// <summary>
/// Verifies build-output dependencies. Reading compiled metadata catches a dependency
/// that implicit usings hide from the source text, and catches transitive framework
/// references a project file does not name.
/// </summary>
[TestClass]
public sealed class AssemblyDependencyTests
{
    private static readonly string[] PersistenceAndWebNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System.ComponentModel.DataAnnotations.Schema",
    ];

    private static readonly string[] HostResourceNamespaces =
    [
        "System.IO",
        "System.Net",
    ];

    private static readonly string[] HostResourceTypes =
    [
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo",
        "System.Environment",
    ];

    [TestMethod]
    public void DomainRefersToNoJulosAssembly()
    {
        AssertNoJulosAssemblyReference(PlatformProjects.Domain);
    }

    [TestMethod]
    public void ContractsRefersToNoJulosAssembly()
    {
        AssertNoJulosAssemblyReference(PlatformProjects.Contracts);
    }

    [TestMethod]
    public void DomainRefersToNoHostResourceType()
    {
        AssertNoTypeIn(
            PlatformProjects.Domain,
            HostResourceNamespaces,
            HostResourceTypes,
            "Domain owns rules only and must not reach the filesystem, the network, a process or the environment.");
    }

    [TestMethod]
    public void InnerLayersReferNoPersistenceOrWebType()
    {
        foreach (var projectFile in new[] { PlatformProjects.Domain, PlatformProjects.Contracts, PlatformProjects.Application })
        {
            AssertNoTypeIn(
                projectFile,
                PersistenceAndWebNamespaces,
                [],
                "Persistence and transport belong to Infrastructure and Server.");
        }
    }

    [TestMethod]
    public void ContractsDeclaresNoSharedFrameworkReference()
    {
        var frameworkReferences = Repository.FrameworkReferences(PlatformProjects.Contracts);

        Assert.AreEqual(
            0,
            frameworkReferences.Count,
            $"Contracts must stay usable from any process, but declares {string.Join(", ", frameworkReferences)}.");
    }

    private static void AssertNoJulosAssemblyReference(string projectFile)
    {
        var references = CompiledAssembly
            .AssemblyReferences(projectFile)
            .Where(name => name.StartsWith("JulOS.", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(
            0,
            references.Length,
            $"{projectFile} must depend on the base class libraries only, but its output refers to {string.Join(", ", references)}.");
    }

    private static void AssertNoTypeIn(
        string projectFile,
        IReadOnlyCollection<string> forbiddenNamespaces,
        IReadOnlyCollection<string> forbiddenTypes,
        string reason)
    {
        var violations = CompiledAssembly
            .TypeReferences(projectFile)
            .Where(type =>
                forbiddenNamespaces.Any(forbidden => IsInNamespace(type.Namespace, forbidden))
                || forbiddenTypes.Contains(type.ToString(), StringComparer.Ordinal))
            .Select(type => type.ToString())
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"{projectFile} refers to {string.Join(", ", violations)}. {reason}");
    }

    private static bool IsInNamespace(string candidate, string forbidden)
    {
        return string.Equals(candidate, forbidden, StringComparison.Ordinal)
            || candidate.StartsWith(forbidden + '.', StringComparison.Ordinal);
    }
}
