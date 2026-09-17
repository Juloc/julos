using JulOS.Agent;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class DockerEngineOptionsTests
{
    [TestMethod]
    public void DockerAccessIsDisabledByDefault()
    {
        var options = DockerEngineOptions.Read(new Dictionary<string, string?>());

        Assert.IsFalse(options.Enabled);
        Assert.IsFalse(options.ControlEnabled);
        Assert.AreEqual(DockerEngineOptions.DefaultSocketPath, options.SocketPath);
    }

    [TestMethod]
    public void ControlCannotBeEnabledWithoutReadAccess()
    {
        var environment = new Dictionary<string, string?>
        {
            ["JULOS_AGENT_DOCKER_CONTROL_ENABLED"] = "true",
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => DockerEngineOptions.Read(environment));
    }

    [TestMethod]
    public void RelativeSocketPathIsRejected()
    {
        var environment = new Dictionary<string, string?>
        {
            ["JULOS_AGENT_DOCKER_ENABLED"] = "true",
            ["JULOS_AGENT_DOCKER_SOCKET_PATH"] = "docker.sock",
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => DockerEngineOptions.Read(environment));
    }
}
