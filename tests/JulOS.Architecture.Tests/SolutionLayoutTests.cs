namespace JulOS.Architecture.Tests;

/// <summary>Verifies that the committed project set and the solution agree.</summary>
[TestClass]
public sealed class SolutionLayoutTests
{
    [TestMethod]
    public void SolutionContainsEveryCommittedProject()
    {
        var committed = Repository.ProjectFiles();
        var listed = Repository.SolutionProjectFiles();

        var missing = committed.Except(listed, StringComparer.Ordinal).ToArray();

        Assert.AreEqual(
            0,
            missing.Length,
            $"These projects exist but are not listed in JulOS.slnx: {string.Join(", ", missing)}.");
    }

    [TestMethod]
    public void SolutionListsOnlyExistingProjects()
    {
        var committed = Repository.ProjectFiles();
        var listed = Repository.SolutionProjectFiles();

        var unknown = listed.Except(committed, StringComparer.Ordinal).ToArray();

        Assert.AreEqual(
            0,
            unknown.Length,
            $"These projects are listed in JulOS.slnx but do not exist: {string.Join(", ", unknown)}.");
    }
}
