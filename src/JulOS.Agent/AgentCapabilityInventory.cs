using System.Runtime.InteropServices;
using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed class AgentCapabilityInventory
{
    private readonly DockerEngineOptions docker;

    internal AgentCapabilityInventory(DockerEngineOptions? docker = null)
    {
        this.docker = docker ?? DockerEngineOptions.Disabled;
    }

    internal IReadOnlyList<AgentCapabilityContract> CreateHeartbeatCapabilities()
    {
        var dockerReadEnabled = OperatingSystem.IsLinux() && this.docker.Enabled;
        var commands = new List<string> { "diagnostics.snapshot" };
        var policies = new List<object>
        {
            new { name = "diagnostics.snapshot", permission = "agent.diagnostics.read", access = "read" },
        };
        if (dockerReadEnabled)
        {
            commands.Add("container.inventory.read");
            commands.Add("container.logs.read");
            policies.Add(new { name = "container.inventory.read", permission = "docker.read", access = "read" });
            policies.Add(new { name = "container.logs.read", permission = "docker.read", access = "read" });
        }
        if (dockerReadEnabled && this.docker.ControlEnabled)
        {
            commands.Add("container.control");
            policies.Add(new { name = "container.control", permission = "docker.control", access = "control" });
        }

        return
        [
            new AgentCapabilityContract(
                "host.metrics.linux",
                1,
                OperatingSystem.IsLinux(),
                1,
                JsonSerializer.SerializeToElement(new
                {
                    operatingSystem = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.OSArchitecture.ToString(),
                })),
            new AgentCapabilityContract(
                "agent.commands.core",
                1,
                Enabled: true,
                MetadataVersion: 1,
                JsonSerializer.SerializeToElement(new { commands })),
            new AgentCapabilityContract(
                "agent.command-policy.core",
                1,
                Enabled: true,
                MetadataVersion: 1,
                JsonSerializer.SerializeToElement(new { commands = policies })),
            new AgentCapabilityContract(
                "agent.diagnostics.core",
                1,
                Enabled: true,
                MetadataVersion: 1,
                JsonSerializer.SerializeToElement(new
                {
                    protocolVersion = AgentProtocolContract.CurrentVersion,
                    updatePreparationContractVersion = AgentUpdateContract.CurrentVersion,
                })),
            new AgentCapabilityContract(
                "docker.read",
                1,
                dockerReadEnabled,
                1,
                JsonSerializer.SerializeToElement(new
                {
                    transport = "unix-socket",
                    operations = new[] { "inventory", "logs" },
                })),
            new AgentCapabilityContract(
                "docker.control",
                1,
                dockerReadEnabled && this.docker.ControlEnabled,
                1,
                JsonSerializer.SerializeToElement(new
                {
                    actions = new[] { "start", "stop", "restart" },
                })),
        ];
    }

    internal IReadOnlyList<AgentCapabilityDiagnosticResponse> CreateDiagnostics() =>
        this.CreateHeartbeatCapabilities()
            .Select(capability => new AgentCapabilityDiagnosticResponse(
                capability.Name,
                capability.Version,
                capability.Enabled,
                capability.MetadataVersion))
            .ToArray();
}
