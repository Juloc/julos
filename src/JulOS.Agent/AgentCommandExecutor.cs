using System.Runtime.InteropServices;
using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed record AgentCommandExecution(bool Succeeded, JsonElement Result, string? ErrorCode);

internal sealed class AgentCommandExecutor
{
    private const string DiagnosticsSnapshotCommand = "diagnostics.snapshot";
    private const string ContainerInventoryReadCommand = "container.inventory.read";
    private const string ContainerLogsReadCommand = "container.logs.read";
    private const string ContainerControlCommand = "container.control";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider timeProvider;
    private readonly string version;
    private readonly AgentCapabilityInventory capabilityInventory;
    private readonly AgentRuntimeDiagnostics diagnostics;
    private readonly DockerEngineClient? docker;

    internal AgentCommandExecutor(
        TimeProvider timeProvider,
        string version,
        AgentCapabilityInventory? capabilityInventory = null,
        AgentRuntimeDiagnostics? diagnostics = null,
        DockerEngineClient? docker = null)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Agent version is required.", nameof(version))
            : version;
        this.capabilityInventory = capabilityInventory ?? new AgentCapabilityInventory();
        this.diagnostics = diagnostics ?? new AgentRuntimeDiagnostics(timeProvider.GetUtcNow());
        this.docker = docker;
    }

    internal async Task<AgentCommandExecution> ExecuteAsync(
        AgentCommandResponse command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (command.ExpiresAtUtc <= this.timeProvider.GetUtcNow())
        {
            return Failure("agent.command_expired");
        }

        try
        {
            return command.CommandType switch
            {
                DiagnosticsSnapshotCommand => Success(this.CreateDiagnosticsSnapshot()),
                ContainerInventoryReadCommand when this.docker is not null =>
                    Success(await this.docker.ReadInventoryAsync(command.Payload, cancellationToken).ConfigureAwait(false)),
                ContainerLogsReadCommand when this.docker is not null =>
                    Success(await this.docker.ReadLogsAsync(command.Payload, cancellationToken).ConfigureAwait(false)),
                ContainerControlCommand when this.docker is not null =>
                    Success(await this.docker.ControlAsync(command.Payload, cancellationToken).ConfigureAwait(false)),
                _ => Failure("agent.command_not_supported"),
            };
        }
        catch (DockerCommandException exception)
        {
            return Failure(exception.Code);
        }
        catch (PlatformNotSupportedException)
        {
            return Failure("docker.platform_not_supported");
        }
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
        new(true, JsonSerializer.SerializeToElement(value, JsonOptions), null);

    private static AgentCommandExecution Failure(string code) =>
        new(false, JsonSerializer.SerializeToElement(new { }, JsonOptions), code);
}
