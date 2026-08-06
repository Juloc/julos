using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Application.Agents;

/// <summary>Durable Agent identity and credential returned only at enrollment.</summary>
/// <param name="AgentId">Created Agent identity.</param>
/// <param name="Credential">Plaintext credential returned only once.</param>
/// <param name="EnrolledAtUtc">Enrollment time.</param>
public sealed record AgentCredential(
    Guid AgentId,
    string Credential,
    DateTimeOffset EnrolledAtUtc);

/// <summary>Authoritative application boundary for Agent identity, commands and telemetry.</summary>
public interface IAgentControlService
{
    /// <summary>Creates one short-lived enrollment token.</summary>
    Task<AgentEnrollmentTokenResponse> CreateEnrollmentTokenAsync(
        Guid actorUserId,
        CreateAgentEnrollmentTokenRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically redeems one enrollment token and issues a durable credential.</summary>
    Task<AgentCredential> RedeemEnrollmentTokenAsync(
        RedeemAgentEnrollmentRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Authenticates an active Agent credential without exposing its stored hash.</summary>
    Task<bool> AuthenticateAsync(
        Guid agentId,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all enrolled Agents.</summary>
    Task<IReadOnlyList<AgentResponse>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads one enrolled Agent.</summary>
    Task<AgentResponse> ReadAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes one Agent and its durable credential.</summary>
    Task<AgentResponse> RevokeAsync(
        Guid actorUserId,
        Guid agentId,
        int revision,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Records liveness, version and capability inventory.</summary>
    Task<AgentResponse> RecordHeartbeatAsync(
        Guid agentId,
        AgentHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Stores one bounded timestamp-preserving metric batch.</summary>
    Task StoreMetricsAsync(
        Guid agentId,
        AgentMetricBatchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads metric series for one bounded range.</summary>
    Task<IReadOnlyList<AgentMetricSeriesResponse>> ReadMetricsAsync(
        Guid agentId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Creates one typed allowlisted command using an idempotency key.</summary>
    Task<AgentCommandResponse> CreateCommandAsync(
        Guid actorUserId,
        Guid agentId,
        CreateAgentCommandRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically acquires the next unexpired command for an Agent.</summary>
    Task<AgentCommandResponse?> AcquireNextCommandAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Completes one acquired command with a bounded result or stable error code.</summary>
    Task<AgentCommandResponse> CompleteCommandAsync(
        Guid agentId,
        Guid commandId,
        CompleteAgentCommandRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable caller-safe failure raised by Agent application rules.</summary>
public sealed class AgentControlException : Exception
{
    /// <summary>Creates an Agent control failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    /// <param name="innerException">Optional server-side cause.</param>
    public AgentControlException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}
