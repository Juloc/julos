using JulOS.Agent;

namespace JulOS.Agent.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AgentIdentityStoreTests
{
    [TestMethod]
    public async Task PendingStateRoundTripsWithPrivateMode()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "identity.json");
            var store = new AgentIdentityStore(path);
            var expected = PendingState();

            await store.SaveAsync(expected, CancellationToken.None);
            var actual = await store.LoadAsync(CancellationToken.None);

            Assert.AreEqual(expected, actual);
            if (OperatingSystem.IsLinux())
            {
                Assert.AreEqual(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(path));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task GroupOrWorldReadableIdentityIsRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Unix permission enforcement is validated on Linux.");
            return;
        }

        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "identity.json");
            var store = new AgentIdentityStore(path);
            await store.SaveAsync(PendingState(), CancellationToken.None);
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => store.LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SymbolicLinkIdentityIsRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("Symbolic-link identity enforcement is validated on Linux.");
            return;
        }

        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "target.json");
            await new AgentIdentityStore(target).SaveAsync(PendingState(), CancellationToken.None);
            var link = Path.Combine(root, "identity.json");
            File.CreateSymbolicLink(link, target);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => new AgentIdentityStore(link).LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task MalformedIdentityIsRejected()
    {
        var root = CreateRoot();
        try
        {
            var path = Path.Combine(root, "identity.json");
            await File.WriteAllTextAsync(path, "{not-json}");
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => new AgentIdentityStore(path).LoadAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AgentProvisioningState PendingState() => new(
        AgentProvisioningStatus.Pending,
        AgentId: null,
        new string('a', 64),
        EnrolledAtUtc: null,
        HeartbeatIntervalSeconds: null,
        CommandPollIntervalSeconds: null,
        "primary-host",
        new string('b', 64),
        "Debian GNU/Linux 13 (trixie)",
        "X64",
        "1.0.0");

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "julos-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
