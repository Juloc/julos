using JulOS.Agent;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentOptionsTests
{
    [TestMethod]
    public void HttpsConfigurationIsAccepted()
    {
        var options = AgentOptions.Read(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "https://os.example.test/",
            ["JULOS_AGENT_ID"] = "0198f5c1-a0f0-7000-8000-000000000101",
            ["JULOS_AGENT_CREDENTIAL"] = new string('a', 48),
            ["JULOS_AGENT_HEARTBEAT_SECONDS"] = "45",
            ["JULOS_AGENT_COMMAND_POLL_SECONDS"] = "3",
        });

        Assert.AreEqual(new Uri("https://os.example.test/"), options.ServerEndpoint);
        Assert.AreEqual(TimeSpan.FromSeconds(45), options.HeartbeatInterval);
        Assert.AreEqual(TimeSpan.FromSeconds(3), options.CommandPollInterval);
    }

    [TestMethod]
    public void NonLoopbackPlainHttpIsRejected()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "http://192.0.2.10:8080/",
            ["JULOS_AGENT_ID"] = "0198f5c1-a0f0-7000-8000-000000000101",
            ["JULOS_AGENT_CREDENTIAL"] = new string('a', 48),
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => AgentOptions.Read(environment));
    }

    [TestMethod]
    public void LoopbackHttpIsAvailableForLocalDevelopment()
    {
        var options = AgentOptions.Read(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["JULOS_SERVER_URL"] = "http://127.0.0.1:8080/",
            ["JULOS_AGENT_ID"] = "0198f5c1-a0f0-7000-8000-000000000101",
            ["JULOS_AGENT_CREDENTIAL"] = new string('a', 48),
        });

        Assert.IsTrue(options.ServerEndpoint.IsLoopback);
    }
}
