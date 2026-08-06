using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed class AgentRuntimeDiagnostics
{
    private readonly object sync = new();
    private readonly DateTimeOffset startedAtUtc;
    private int connectionAttempts;
    private int successfulHeartbeats;
    private int consecutiveFailures;
    private DateTimeOffset? lastConnectedAtUtc;
    private DateTimeOffset? lastFailureAtUtc;
    private string? lastFailureKind;
    private int? nextRetryDelaySeconds;

    internal AgentRuntimeDiagnostics(DateTimeOffset startedAtUtc)
    {
        this.startedAtUtc = startedAtUtc;
    }

    internal DateTimeOffset StartedAtUtc => this.startedAtUtc;

    internal void RecordConnectionAttempt()
    {
        lock (this.sync)
        {
            this.connectionAttempts = checked(this.connectionAttempts + 1);
        }
    }

    internal void RecordHeartbeatSucceeded(DateTimeOffset observedAtUtc)
    {
        lock (this.sync)
        {
            this.successfulHeartbeats = checked(this.successfulHeartbeats + 1);
            this.consecutiveFailures = 0;
            this.lastConnectedAtUtc = observedAtUtc;
            this.nextRetryDelaySeconds = null;
        }
    }

    internal void RecordConnectionFailure(
        DateTimeOffset observedAtUtc,
        string failureKind,
        TimeSpan retryDelay)
    {
        if (string.IsNullOrWhiteSpace(failureKind)
            || failureKind != failureKind.Trim()
            || failureKind.Length > 64
            || failureKind.Any(char.IsControl))
        {
            throw new ArgumentException("Reconnect failure kind is invalid.", nameof(failureKind));
        }
        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        lock (this.sync)
        {
            this.consecutiveFailures = checked(this.consecutiveFailures + 1);
            this.lastFailureAtUtc = observedAtUtc;
            this.lastFailureKind = failureKind;
            this.nextRetryDelaySeconds = checked((int)Math.Ceiling(retryDelay.TotalSeconds));
        }
    }

    internal AgentReconnectDiagnosticsResponse Snapshot()
    {
        lock (this.sync)
        {
            return new AgentReconnectDiagnosticsResponse(
                this.connectionAttempts,
                this.successfulHeartbeats,
                this.consecutiveFailures,
                this.lastConnectedAtUtc,
                this.lastFailureAtUtc,
                this.lastFailureKind,
                this.nextRetryDelaySeconds);
        }
    }
}
