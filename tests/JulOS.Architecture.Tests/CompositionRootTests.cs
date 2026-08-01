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

    [TestMethod]
    public void NoProjectReferencesTheServer()
    {
        var violations = Repository
            .ProjectFiles()
            .Where(projectFile => Repository.ProjectReferences(projectFile).Contains(PlatformProjects.Server, StringComparer.Ordinal))
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"The composition root is referenced by nothing, but is referenced by {string.Join(", ", violations)}.");
    }
}
