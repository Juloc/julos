using System.Text.Json;

namespace JulOS.Contracts.Agents;

/// <summary>Creates a short-lived one-time Agent enrollment token.</summary>
/// <param name="Description">Administrator-visible token purpose.</param>
/// <param name="LifetimeMinutes">Requested token lifetime in minutes.</param>
public sealed record CreateAgentEnrollmentTokenRequest(
    string Description,
    int LifetimeMinutes);

/// <summary>One newly issued Agent enrollment token.</summary>
/// <param name="TokenId">Server-side token identity.</param>
/// <param name="Token">Plaintext token returned only at creation.</param>
/// <param name="ExpiresAtUtc">Absolute token expiry.</param>
public sealed record AgentEnrollmentTokenResponse(
    Guid TokenId,
    string Token,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Redeems one enrollment token and declares immutable Agent identity facts.</summary>
/// <param name="Token">One-time enrollment token.</param>
/// <param name="Name">Administrator-visible Agent name.</param>
/// <param name="MachineIdentity">Stable host machine identity.</param>
/// <param name="OperatingSystem">Agent operating-system description.</param>
/// <param name="Architecture">Agent processor architecture.</param>
/// <param name="Version">Agent software version.</param>
public sealed record RedeemAgentEnrollmentRequest(
    string Token,
    string Name,
    string MachineIdentity,
    string OperatingSystem,
    string Architecture,
    string Version);

/// <summary>Durable identity and credential returned after enrollment.</summary>
/// <param name="AgentId">Created Agent identity.</param>
/// <param name="Credential">Plaintext durable credential returned only at issuance.</param>
/// <param name="EnrolledAtUtc">Enrollment time.</param>
/// <param name="HeartbeatIntervalSeconds">Required heartbeat interval.</param>
/// <param name="CommandPollIntervalSeconds">Required command polling interval.</param>
public sealed record RedeemAgentEnrollmentResponse(
    Guid AgentId,
    string Credential,
    DateTimeOffset EnrolledAtUtc,
    int HeartbeatIntervalSeconds,
    int CommandPollIntervalSeconds);

/// <summary>Current control-plane view of one Agent.</summary>
/// <param name="AgentId">Agent identity.</param>
/// <param name="Name">Administrator-visible name.</param>
/// <param name="MachineIdentity">Stable host machine identity.</param>
/// <param name="OperatingSystem">Operating-system description.</param>
/// <param name="Architecture">Processor architecture.</param>
/// <param name="Version">Agent software version.</param>
/// <param name="State">Current lifecycle or connectivity state.</param>
/// <param name="EnrolledAtUtc">Enrollment time.</param>
/// <param name="LastSeenAtUtc">Most recent authenticated observation.</param>
/// <param name="RevokedAtUtc">Credential revocation time.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
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

/// <summary>One versioned capability advertised by an Agent.</summary>
/// <param name="Name">Capability identity.</param>
/// <param name="Version">Capability contract version.</param>
/// <param name="Enabled">Whether the capability can accept work.</param>
/// <param name="MetadataVersion">Version of the capability metadata shape.</param>
/// <param name="Metadata">Capability-specific metadata.</param>
public sealed record AgentCapabilityContract(
    string Name,
    int Version,
    bool Enabled,
    int MetadataVersion,
    JsonElement Metadata);

/// <summary>Periodic Agent identity, capability and liveness observation.</summary>
/// <param name="Version">Agent software version.</param>
/// <param name="Capabilities">Advertised capabilities.</param>
/// <param name="ObservedAtUtc">Agent-side observation time.</param>
public sealed record AgentHeartbeatRequest(
    string Version,
    IReadOnlyList<AgentCapabilityContract> Capabilities,
    DateTimeOffset ObservedAtUtc);

/// <summary>One timestamped Agent metric observation.</summary>
/// <param name="Name">Metric identity.</param>
/// <param name="Value">Observed value, or unknown when unavailable.</param>
/// <param name="Unit">Metric unit.</param>
/// <param name="Labels">Bounded identifying labels.</param>
/// <param name="ObservedAtUtc">Agent-side observation time.</param>
public sealed record AgentMetricContract(
    string Name,
    double? Value,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset ObservedAtUtc);

/// <summary>One bounded batch of Agent metric observations.</summary>
/// <param name="Metrics">Metrics in the batch.</param>
public sealed record AgentMetricBatchRequest(
    IReadOnlyList<AgentMetricContract> Metrics);

/// <summary>Creates one typed allowlisted Agent command.</summary>
/// <param name="OperationKey">Idempotency key within the Agent.</param>
/// <param name="CommandType">Allowlisted command contract identity.</param>
/// <param name="Payload">Versioned typed command payload.</param>
/// <param name="LifetimeSeconds">Maximum command lifetime.</param>
public sealed record CreateAgentCommandRequest(
    string OperationKey,
    string CommandType,
    JsonElement Payload,
    int LifetimeSeconds);

/// <summary>Current state and safe result of one Agent command.</summary>
/// <param name="CommandId">Command identity.</param>
/// <param name="AgentId">Owning Agent identity.</param>
/// <param name="OperationKey">Idempotency key.</param>
/// <param name="CommandType">Typed command identity.</param>
/// <param name="Payload">Versioned command payload.</param>
/// <param name="State">Current command state.</param>
/// <param name="CreatedAtUtc">Creation time.</param>
/// <param name="ExpiresAtUtc">Absolute deadline.</param>
/// <param name="StartedAtUtc">Execution start time.</param>
/// <param name="CompletedAtUtc">Terminal completion time.</param>
/// <param name="Result">Bounded safe result.</param>
/// <param name="ErrorCode">Stable failure code.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
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

/// <summary>Completes one leased Agent command.</summary>
/// <param name="Succeeded">Whether execution succeeded.</param>
/// <param name="Result">Bounded safe result payload.</param>
/// <param name="ErrorCode">Stable failure code when unsuccessful.</param>
/// <param name="Revision">Expected command revision.</param>
public sealed record CompleteAgentCommandRequest(
    bool Succeeded,
    JsonElement Result,
    string? ErrorCode,
    int Revision);

/// <summary>One metric series returned for an Agent.</summary>
/// <param name="AgentId">Agent identity.</param>
/// <param name="Name">Metric identity.</param>
/// <param name="Unit">Metric unit.</param>
/// <param name="Labels">Series labels.</param>
/// <param name="Points">Timestamped observations.</param>
public sealed record AgentMetricSeriesResponse(
    Guid AgentId,
    string Name,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<AgentMetricPointResponse> Points);

/// <summary>One timestamped metric-series point.</summary>
/// <param name="ObservedAtUtc">Original Agent-side observation time.</param>
/// <param name="Value">Observed value, or unknown when unavailable.</param>
public sealed record AgentMetricPointResponse(
    DateTimeOffset ObservedAtUtc,
    double? Value);
