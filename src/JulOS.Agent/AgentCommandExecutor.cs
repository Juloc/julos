using System.Runtime.InteropServices;
using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed record AgentCommandExecution(
    bool Succeeded,
    JsonElement Result,
    string? ErrorCode);

internal sealed class AgentCommandExecutor
{
    private const string DiagnosticsSnapshotCommand = "diagnostics.snapshot";
    private readonly TimeProvider timeProvider;
    private readonly string version;
    private readonly AgentCapabilityInventory capabilityInventory;
    private readonly AgentRuntimeDiagnostics diagnostics;

    internal AgentCommandExecutor(
        TimeProvider timeProvider,
        string version,
        AgentCapabilityInventory? capabilityInventory = null,
        AgentRuntimeDiagnostics? diagnostics = null)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Agent version is required.", nameof(version))
            : version;
        this.capabilityInventory = capabilityInventory ?? new AgentCapabilityInventory();
        this.diagnostics = diagnostics ?? new AgentRuntimeDiagnostics(timeProvider.GetUtcNow());
    }

    internal Task<AgentCommandExecution> ExecuteAsync(
        AgentCommandResponse command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.ExpiresAtUtc <= this.timeProvider.GetUtcNow())
        {
            return Task.FromResult(Failure("agent.command_expired"));
        }

        return command.CommandType switch
        {
            DiagnosticsSnapshotCommand => Task.FromResult(Success(this.CreateDiagnosticsSnapshot())),
            _ => Task.FromResult(Failure("agent.command_not_supported")),
        };
    }

    private AgentDiagnosticsSnapshotResponse CreateDiagnosticsSnapshot() => new(
        this.version,
        AgentProtocolContract.CurrentVersion,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription,
        this.diagnostics.StartedAtUtc,
        this.timeProvider.GetUtcNow(),
        this.capabilityInventory.CreateDiagnostics(),
        this.diagnostics.Snapshot(),
        new AgentUpdateContractResponse(
            AgentUpdateContract.CurrentVersion,
            AgentUpdateContract.AutomaticDownloadSupported,
            AgentUpdateContract.AutomaticApplySupported,
            AgentUpdateContract.AutomaticRestartSupported));

    private static AgentCommandExecution Success<T>(T value) =>
        new(true, JsonSerializer.SerializeToElement(value), null);

    private static AgentCommandExecution Failure(string code) =>
        new(false, JsonSerializer.SerializeToElement(new { }), code);
}
