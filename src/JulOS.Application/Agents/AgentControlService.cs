using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Application.Agents;

public sealed record AgentCredential(
    Guid AgentId,
    string Credential,
    DateTimeOffset EnrolledAtUtc);

public interface IAgentControlService
{
    Task<AgentEnrollmentTokenResponse> CreateEnrollmentTokenAsync(
        Guid actorUserId,
        CreateAgentEnrollmentTokenRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    Task<AgentCredential> RedeemEnrollmentTokenAsync(
        RedeemAgentEnrollmentRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    Task<bool> AuthenticateAsync(
        Guid agentId,
        ReadOnlyMemory<byte> credential,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentResponse>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<AgentResponse> ReadAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    Task<AgentResponse> RevokeAsync(
        Guid actorUserId,
        Guid agentId,
        int revision,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    Task<AgentResponse> RecordHeartbeatAsync(
        Guid agentId,
        AgentHeartbeatRequest request,
        CancellationToken cancellationToken = default);

    Task StoreMetricsAsync(
        Guid agentId,
        AgentMetricBatchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentMetricSeriesResponse>> ReadMetricsAsync(
        Guid agentId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResponse> CreateCommandAsync(
        Guid actorUserId,
        Guid agentId,
        CreateAgentCommandRequest request,
        string correlationId,
        string? remoteAddress,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResponse?> AcquireNextCommandAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    Task<AgentCommandResponse> CompleteCommandAsync(
        Guid agentId,
        Guid commandId,
        CompleteAgentCommandRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AgentControlException : Exception
{
    public AgentControlException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    public string Code { get; }
}
