namespace JulOS.Architecture.Tests;

/// <summary>Verifies that JulOS Server is the only web composition root in the control plane.</summary>
[TestClass]
public sealed class CompositionRootTests
{
    private const string WebSdk = "Microsoft.NET.Sdk.Web";

    [TestMethod]
    public void ServerIsTheOnlyWebProject()
    {
        var webProjects = Repository
            .ProjectFiles()
            .Where(projectFile => string.Equals(Repository.SdkAttribute(projectFile), WebSdk, StringComparison.Ordinal))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { PlatformProjects.Server },
            webProjects,
            $"Only JulOS.Server hosts the web application, but '{WebSdk}' is used by {string.Join(", ", webProjects)}.");
    }

    /// <summary>
    /// Only a test may reference the composition root, and only to host it. Production
    /// code that reaches into Server would turn the host into a shared library.
    /// </summary>
    [TestMethod]
    public void OnlyTestsReferenceTheServer()
    {
        var violations = Repository
            .ProjectFiles()
            .Where(projectFile => !projectFile.StartsWith("tests/", StringComparison.Ordinal))
            .Where(projectFile => Repository.ProjectReferences(projectFile).Contains(PlatformProjects.Server, StringComparer.Ordinal))
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"The composition root must be referenced by nothing but tests, and is referenced by {string.Join(", ", violations)}.");
    }
}
