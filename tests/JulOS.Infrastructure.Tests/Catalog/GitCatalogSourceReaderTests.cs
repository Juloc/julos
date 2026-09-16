using JulOS.Application.Catalog;
using JulOS.Domain.Catalog;
using JulOS.Infrastructure.Catalog;

namespace JulOS.Infrastructure.Tests.Catalog;

/// <summary>
/// CAT-002: what the Git adapter accepts as a source, and what it hands to git.
/// </summary>
/// <remarks>
/// The clone itself needs a reachable remote, so what is asserted here is everything that
/// decides whether that clone is safe: which locations are accepted, what the argument vector
/// looks like, and that a failed clone leaves nothing behind.
/// </remarks>
[TestClass]
public sealed class GitCatalogSourceReaderTests
{
    /// <summary>The exact vector one shallow single-branch clone is made of.</summary>
    private static readonly string[] ExpectedCloneArguments =
    [
        "clone",
        "--depth=1",
        "--single-branch",
        "--no-tags",
        "--recurse-submodules=no",
        "--quiet",
        "--branch",
        "release/2026",
        "--",
        "https://example.test/repo.git",
        "/tmp/work",
    ];

    private readonly List<string> directories = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var directory in this.directories.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A directory the operating system still holds open is test litter.
            }
        }
    }

    [TestMethod]
    public void ARemoteAndAnOptionalReferenceAreSplitApart()
    {
        Assert.AreEqual(
            ("https://example.test/juloc/catalog.git", (string?)null),
            GitCatalogSourceReader.Parse("https://example.test/juloc/catalog.git"));
        Assert.AreEqual(
            ("https://example.test/juloc/catalog.git", (string?)"release/2026"),
            GitCatalogSourceReader.Parse("https://example.test/juloc/catalog.git#release/2026"));
    }

    [TestMethod]
    public void OnlyAnHttpsRemoteIsAccepted()
    {
        // An SSH remote authenticates with a private key, and supporting it would mean
        // writing that key to disk for the duration of a refresh.
        foreach (var location in new[]
        {
            "git@example.test:juloc/catalog.git",
            "ssh://example.test/juloc/catalog.git",
            "http://example.test/juloc/catalog.git",
            "file:///tmp/catalog",
            "example.test/juloc/catalog.git",
        })
        {
            var failure = Assert.ThrowsExactly<CatalogRefreshException>(
                () => GitCatalogSourceReader.Parse(location),
                $"'{location}' is not an https remote.");
            Assert.AreEqual(CatalogRefreshException.SourceUnavailable, failure.Code);
        }
    }

    [TestMethod]
    public void AReferenceThatCouldBecomeAnOptionOrEscapeIsRefused()
    {
        foreach (var reference in new[] { "--upload-pack=touch", "main..other", "main;rm", "main$(id)" })
        {
            var failure = Assert.ThrowsExactly<CatalogRefreshException>(
                () => GitCatalogSourceReader.Parse($"https://example.test/repo.git#{reference}"),
                $"'{reference}' is not a plain Git reference.");
            Assert.AreEqual(CatalogRefreshException.SourceUnavailable, failure.Code);
        }
    }

    [TestMethod]
    public void TheCloneFetchesOneStateAndNoSubmodules()
    {
        var arguments = GitCatalogSourceReader.CloneArguments(
            "https://example.test/repo.git",
            "release/2026",
            "/tmp/work");

        CollectionAssert.AreEqual(
            ExpectedCloneArguments,
            arguments,
            "A submodule points at another repository, and history is not part of a catalog.");

        Assert.AreEqual(
            "--",
            GitCatalogSourceReader.CloneArguments("https://example.test/repo.git", null, "/tmp/work")[^3],
            "The remote stays positional, so a location beginning with a hyphen cannot become a flag.");
    }

    [TestMethod]
    public async Task AnUnreachableRemoteFailsAndLeavesNoWorkingCopy()
    {
        var workingRoot = Path.Combine(
            Path.GetTempPath(),
            "julos-catalog-git",
            Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(workingRoot);
        this.directories.Add(workingRoot);
        var reader = new GitCatalogSourceReader(workingRoot);

        // `.invalid` is reserved and never resolves, so this exercises the real invocation
        // without needing a network that answers.
        var failure = await Assert.ThrowsExactlyAsync<CatalogRefreshException>(
            () => reader.OpenAsync(
                new CatalogSourceLocation(CatalogSourceKind.Git, "https://catalog.invalid/repo.git", null),
                null));

        Assert.AreEqual(CatalogRefreshException.SourceUnavailable, failure.Code);
        Assert.AreEqual(
            0,
            Directory.GetDirectories(workingRoot).Length,
            "A failed clone leaves no partial working copy behind.");
    }
}
