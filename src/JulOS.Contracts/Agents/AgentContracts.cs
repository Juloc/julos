using System.Text.Json;

namespace JulOS.Contracts.Agents;

public sealed record CreateAgentEnrollmentTokenRequest(
    string Description,
    int LifetimeMinutes);

public sealed record AgentEnrollmentTokenResponse(
    Guid TokenId,
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record RedeemAgentEnrollmentRequest(
    string Token,
    string Name,
    string MachineIdentity,
    string OperatingSystem,
    string Architecture,
    string Version);

public sealed record RedeemAgentEnrollmentResponse(
    Guid AgentId,
    string Credential,
    DateTimeOffset EnrolledAtUtc,
    int HeartbeatIntervalSeconds,
    int CommandPollIntervalSeconds);

public sealed record AgentResponse(
    Guid AgentId,
    string Name,
    string MachineIdentity,
    string OperatingSystem,
    string Architecture,
    string Version,
    string State,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset? RevokedAtUtc,
    int Revision);

public sealed record AgentCapabilityContract(
    string Name,
    int Version,
    bool Enabled,
    int MetadataVersion,
    JsonElement Metadata);

public sealed record AgentHeartbeatRequest(
    string Version,
    IReadOnlyList<AgentCapabilityContract> Capabilities,
    DateTimeOffset ObservedAtUtc);

public sealed record AgentMetricContract(
    string Name,
    double? Value,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset ObservedAtUtc);

public sealed record AgentMetricBatchRequest(
    IReadOnlyList<AgentMetricContract> Metrics);

public sealed record CreateAgentCommandRequest(
    string OperationKey,
    string CommandType,
    JsonElement Payload,
    int LifetimeSeconds);

public sealed record AgentCommandResponse(
    Guid CommandId,
    Guid AgentId,
    string OperationKey,
    string CommandType,
    JsonElement Payload,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    JsonElement? Result,
    string? ErrorCode,
    int Revision);

public sealed record CompleteAgentCommandRequest(
    bool Succeeded,
    JsonElement Result,
    string? ErrorCode,
    int Revision);

public sealed record AgentMetricSeriesResponse(
    Guid AgentId,
    string Name,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<AgentMetricPointResponse> Points);

public sealed record AgentMetricPointResponse(
    DateTimeOffset ObservedAtUtc,
    double? Value);
