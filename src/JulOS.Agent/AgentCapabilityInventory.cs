using System.Runtime.InteropServices;
using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed class AgentCapabilityInventory
{
    private static readonly string[] SupportedCommands =
    [
        "diagnostics.snapshot",
    ];

    internal IReadOnlyList<AgentCapabilityContract> CreateHeartbeatCapabilities() =>
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
            JsonSerializer.SerializeToElement(new
            {
                commands = SupportedCommands,
            })),
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
    ];

    internal IReadOnlyList<AgentCapabilityDiagnosticResponse> CreateDiagnostics() =>
        this.CreateHeartbeatCapabilities()
            .Select(capability => new AgentCapabilityDiagnosticResponse(
                capability.Name,
                capability.Version,
                capability.Enabled,
                capability.MetadataVersion))
            .ToArray();
}
