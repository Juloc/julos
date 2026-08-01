namespace JulOS.Architecture.Tests;

/// <summary>Verifies the dependency direction defined in <c>docs/ARCHITECTURE.md</c>.</summary>
[TestClass]
public sealed class ProjectReferenceTests
{
    [TestMethod]
    public void DomainReferencesNoOtherProject()
    {
        var references = Repository.ProjectReferences("src/JulOS.Domain/JulOS.Domain.csproj");

        Assert.AreEqual(
            0,
            references.Count,
            $"JulOS.Domain must depend on the base class libraries only, but references {string.Join(", ", references)}.");
    }

    [TestMethod]
    public void ContractsReferenceNoOtherProject()
    {
        var references = Repository.ProjectReferences("src/JulOS.Contracts/JulOS.Contracts.csproj");

        Assert.AreEqual(
            0,
            references.Count,
            $"JulOS.Contracts must stay transport-neutral, but references {string.Join(", ", references)}.");
    }
}
