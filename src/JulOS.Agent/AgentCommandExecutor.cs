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

    internal AgentCommandExecutor(TimeProvider timeProvider, string version)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.version = string.IsNullOrWhiteSpace(version)
            ? throw new ArgumentException("Agent version is required.", nameof(version))
            : version;
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
            DiagnosticsSnapshotCommand => Task.FromResult(Success(new
            {
                version = this.version,
                operatingSystem = Environment.OSVersion.Platform.ToString(),
                architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                observedAtUtc = this.timeProvider.GetUtcNow(),
            })),
            _ => Task.FromResult(Failure("agent.command_not_supported")),
        };
    }

    private static AgentCommandExecution Success<T>(T value) =>
        new(true, JsonSerializer.SerializeToElement(value), null);

    private static AgentCommandExecution Failure(string code) =>
        new(false, JsonSerializer.SerializeToElement(new { }), code);
}
