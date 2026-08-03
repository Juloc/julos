using JulOS.Agent;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentOptionsTests
{
    [TestMethod]
    public void HttpsBootstrapConfigurationIsAccepted()
    {
        var identityPath = Path.Combine(Path.GetTempPath(), "julos-agent-tests", "identity.json");
        var options = AgentBootstrapOptions.Read(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "https://os.example.test/",
            ["JULOS_AGENT_IDENTITY_PATH"] = identityPath,
            ["JULOS_AGENT_ENROLLMENT_TOKEN"] = new string('a', 64),
            ["JULOS_AGENT_NAME"] = "primary-host",
            ["JULOS_AGENT_VERSION"] = "1.0.0",
            ["JULOS_AGENT_HEARTBEAT_SECONDS"] = "45",
            ["JULOS_AGENT_COMMAND_POLL_SECONDS"] = "3",
        });

        Assert.AreEqual(new Uri("https://os.example.test/"), options.ServerEndpoint);
        Assert.AreEqual(Path.GetFullPath(identityPath), options.IdentityPath);
        Assert.AreEqual("primary-host", options.Name);
        Assert.AreEqual(TimeSpan.FromSeconds(45), options.HeartbeatIntervalOverride);
        Assert.AreEqual(TimeSpan.FromSeconds(3), options.CommandPollIntervalOverride);
    }

    [TestMethod]
    public void NonLoopbackPlainHttpIsRejected()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "http://192.0.2.10:8080/",
            ["JULOS_AGENT_IDENTITY_PATH"] = Path.Combine(Path.GetTempPath(), "identity.json"),
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => AgentBootstrapOptions.Read(environment));
    }

    [TestMethod]
    public void LoopbackHttpIsAvailableForLocalDevelopment()
    {
        var options = AgentBootstrapOptions.Read(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "http://127.0.0.1:8080/",
            ["JULOS_AGENT_IDENTITY_PATH"] = Path.Combine(Path.GetTempPath(), "identity.json"),
        });

        Assert.IsTrue(options.ServerEndpoint.IsLoopback);
    }

    [TestMethod]
    public void RuntimeOptionsUsePersistedServerIntervalsUnlessOverridden()
    {
        var bootstrap = AgentBootstrapOptions.Read(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "https://os.example.test/",
            ["JULOS_AGENT_IDENTITY_PATH"] = Path.Combine(Path.GetTempPath(), "identity.json"),
            ["JULOS_AGENT_VERSION"] = "1.0.0",
        });
        var identity = new AgentProvisioningState(
            AgentProvisioningStatus.Enrolled,
            Guid.Parse("0198f5c1-a0f0-7000-8000-000000000101"),
            new string('a', 64),
            new DateTimeOffset(2026, 8, 3, 17, 0, 0, TimeSpan.Zero),
            30,
            5,
            "primary-host",
            new string('b', 64),
            "Debian GNU/Linux 13 (trixie)",
            "X64",
            "1.0.0");

        var options = AgentOptions.Create(bootstrap, identity);

        Assert.AreEqual(identity.AgentId, options.AgentId);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.HeartbeatInterval);
        Assert.AreEqual(TimeSpan.FromSeconds(5), options.CommandPollInterval);
    }
}
