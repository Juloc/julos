namespace JulOS.Architecture.Tests;

/// <summary>
/// Verifies that product-specific knowledge stays in packages, Agent capabilities and
/// runtime components. Core owns platform concepts only.
/// </summary>
[TestClass]
public sealed class ProductTerminologyTests
{
    private static readonly string[] ProductTerms =
    [
        "Docker",
        "Proxmox",
        "Caddy",
        "Guacamole",
        "Chromium",
        "Rdp",
        "Vnc",
        "Ssh",
        "Smb",
        "Sftp",
        "WebDav",
    ];

    /// <summary>
    /// The Agent implements the host side of the Docker capability family, so Docker is
    /// the one product it is allowed to name. Everything else stays in package workers.
    /// </summary>
    private static readonly string[] AgentForbiddenTerms =
        [.. ProductTerms.Where(term => !string.Equals(term, "Docker", StringComparison.Ordinal))];

    [TestMethod]
    public void CoreNamesNoProduct()
    {
        var violations = PlatformProjects.Core
            .SelectMany(projectFile => SourceScanner.Find(projectFile, ProductTerms))
            .ToArray();

        Assert.AreEqual(
            0,
            violations.Length,
            $"Core must not name a product: {string.Join(", ", violations.Select(match => match.ToString()))}.");
    }

    [TestMethod]
    public void AgentNamesNoProductBesidesDocker()
    {
        var violations = SourceScanner.Find(PlatformProjects.Agent, AgentForbiddenTerms);

        Assert.AreEqual(
            0,
            violations.Count,
            $"The Agent carries transport and host capabilities, not package logic: {string.Join(", ", violations.Select(match => match.ToString()))}.");
    }

    [TestMethod]
    public void PackageSdkNamesNoProduct()
    {
        var violations = SourceScanner.Find(PlatformProjects.PackageSdk, ProductTerms);

        Assert.AreEqual(
            0,
            violations.Count,
            $"The Package SDK serves every package equally: {string.Join(", ", violations.Select(match => match.ToString()))}.");
    }

    [TestMethod]
    public void ScannerFindsCompleteIdentifierSegments()
    {
        var pattern = SourceScanner.IdentifierPattern(["Docker"]);

        Assert.IsTrue(pattern.IsMatch("var client = new DockerClient();"), "A leading identifier segment must be found.");
        Assert.IsTrue(pattern.IsMatch("var host = useDockerHost;"), "An inner PascalCase segment must be found.");
        Assert.IsTrue(pattern.IsMatch("// docker is not allowed here"), "A product name in a comment must be found.");
        Assert.IsFalse(pattern.IsMatch("var value = undockered;"), "A term inside a lower-case word must not be reported.");
    }

    [TestMethod]
    public void ScannerIgnoresAccidentalLetterSequences()
    {
        var pattern = SourceScanner.IdentifierPattern(["Rdp", "Ssh"]);

        Assert.IsFalse(pattern.IsMatch("var value = hardPath;"), "'hardPath' must not be read as RDP.");
        Assert.IsFalse(pattern.IsMatch("var value = new PressHandler();"), "'PressHandler' must not be read as SSH.");
        Assert.IsTrue(pattern.IsMatch("var session = new RdpSession();"), "A real RDP identifier must still be found.");
        Assert.IsTrue(pattern.IsMatch("var session = openSshSession();"), "A real SSH identifier must still be found.");
    }
}
