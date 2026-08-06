using System.Text.Json;
using System.Text.RegularExpressions;

namespace JulOS.Architecture.Tests;

[TestClass]
public sealed partial class BrowserRuntimeTests
{
    [TestMethod]
    public void BrowserRuntimeIsPinnedBoundedAndCredentialFree()
    {
        var runtimeDirectory = Path.Combine(
            Repository.Root,
            "packages",
            "JulOS.Browser",
            "runtime");
        var dockerfile = File.ReadAllText(Path.Combine(runtimeDirectory, "Dockerfile"));
        var launcher = File.ReadAllText(Path.Combine(runtimeDirectory, "browser-runtime.sh"));
        using var definition = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(runtimeDirectory, "runtime-definition.json")));
        var root = definition.RootElement;
        var limits = root.GetProperty("Limits");

        StringAssert.Matches(dockerfile, DigestPinnedBaseImage());
        StringAssert.Contains(dockerfile, "FROM ${DEBIAN_IMAGE}");
        StringAssert.Matches(dockerfile, DebianSnapshotPin());
        StringAssert.Matches(dockerfile, DebianSecuritySnapshotPin());
        StringAssert.Contains(dockerfile, "snapshot.debian.org/archive/debian/${DEBIAN_SNAPSHOT}");
        StringAssert.Contains(dockerfile, "snapshot.debian.org/archive/debian-security/${DEBIAN_SECURITY_SNAPSHOT}");
        StringAssert.Contains(dockerfile, "ARG CHROMIUM_VERSION=");
        StringAssert.Contains(dockerfile, "USER 10001:10001");
        StringAssert.Contains(dockerfile, "EXPOSE 5900/tcp");
        StringAssert.Contains(dockerfile, "HEALTHCHECK");
        Assert.IsFalse(dockerfile.Contains("JULOS_VNC_PASSWORD=", StringComparison.Ordinal));

        StringAssert.Contains(launcher, "JULOS_VNC_PASSWORD is required.");
        StringAssert.Contains(launcher, "unset JULOS_VNC_PASSWORD");
        StringAssert.Contains(launcher, "-no6");
        StringAssert.Contains(launcher, "nc -z 127.0.0.1 5900");
        StringAssert.Contains(launcher, "rm -rf \"$runtime_directory\"");

        Assert.AreEqual("de.juloc.julos.browser", root.GetProperty("PackageId").GetString());
        Assert.AreEqual("configured-isolated", root.GetProperty("NetworkPolicy").GetString());
        Assert.AreEqual("vnc", root.GetProperty("DisplayProtocol").GetString());
        Assert.AreEqual(5900, root.GetProperty("DisplayPort").GetInt32());
        Assert.AreEqual(1024, limits.GetProperty("MemoryMegabytes").GetInt32());
        Assert.AreEqual(2.0m, limits.GetProperty("CpuLimit").GetDecimal());
        Assert.AreEqual(256, limits.GetProperty("PidsLimit").GetInt32());
        CollectionAssert.Contains(
            root.GetProperty("RequiredSecretEnvironment")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray(),
            "JULOS_VNC_PASSWORD");
    }

    [GeneratedRegex(@"ARG DEBIAN_IMAGE=\S+@sha256:[0-9a-f]{64}")]
    private static partial Regex DigestPinnedBaseImage();

    [GeneratedRegex(@"ARG DEBIAN_SNAPSHOT=[0-9]{8}T[0-9]{6}Z")]
    private static partial Regex DebianSnapshotPin();

    [GeneratedRegex(@"ARG DEBIAN_SECURITY_SNAPSHOT=[0-9]{8}T[0-9]{6}Z")]
    private static partial Regex DebianSecuritySnapshotPin();
}
