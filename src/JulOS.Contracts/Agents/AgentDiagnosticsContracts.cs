namespace JulOS.Contracts.Agents;

/// <summary>Stable transport-level compatibility contract for JulOS Agents.</summary>
public static class AgentProtocolContract
{
    /// <summary>HTTP request and response header carrying the protocol version.</summary>
    public const string HeaderName = "X-JulOS-Agent-Protocol";

    /// <summary>HTTP response header describing the oldest supported protocol.</summary>
    public const string MinimumHeaderName = "X-JulOS-Agent-Protocol-Min";

    /// <summary>HTTP response header describing the newest supported protocol.</summary>
    public const string MaximumHeaderName = "X-JulOS-Agent-Protocol-Max";

    /// <summary>Current JulOS 1.0 Agent protocol.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Oldest protocol accepted by this release.</summary>
    public const int MinimumSupportedVersion = 1;

    /// <summary>Newest protocol accepted by this release.</summary>
    public const int MaximumSupportedVersion = 1;

    /// <summary>Returns whether one protocol version is accepted without negotiation or downgrade.</summary>
    public static bool IsSupported(int version) =>
        version is >= MinimumSupportedVersion and <= MaximumSupportedVersion;
}

/// <summary>Stable future Agent update preparation contract without automatic installation behavior.</summary>
public static class AgentUpdateContract
{
    /// <summary>Current update preparation contract version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Automatic artifact download is intentionally unsupported.</summary>
    public const bool AutomaticDownloadSupported = false;

    /// <summary>Automatic binary replacement is intentionally unsupported.</summary>
    public const bool AutomaticApplySupported = false;

    /// <summary>Automatic service restart is intentionally unsupported.</summary>
    public const bool AutomaticRestartSupported = false;
}

/// <summary>One capability entry included in a bounded Agent diagnostics snapshot.</summary>
public sealed record AgentCapabilityDiagnosticResponse(
    string Name,
    int Version,
    bool Enabled,
    int MetadataVersion);

/// <summary>Safe reconnect history retained in memory by the Agent process.</summary>
public sealed record AgentReconnectDiagnosticsResponse(
    int ConnectionAttempts,
    int SuccessfulHeartbeats,
    int ConsecutiveFailures,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastFailureAtUtc,
    string? LastFailureKind,
    int? NextRetryDelaySeconds);

/// <summary>Describes the future manual Agent update preparation boundary.</summary>
public sealed record AgentUpdateContractResponse(
    int ContractVersion,
    bool AutomaticDownloadSupported,
    bool AutomaticApplySupported,
    bool AutomaticRestartSupported);

/// <summary>Bounded actionable Agent diagnostics returned by the allowlisted snapshot command.</summary>
public sealed record AgentDiagnosticsSnapshotResponse(
    string Version,
    int ProtocolVersion,
    string OperatingSystem,
    string Architecture,
    string Framework,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<AgentCapabilityDiagnosticResponse> Capabilities,
    AgentReconnectDiagnosticsResponse Reconnect,
    AgentUpdateContractResponse UpdateContract);

/// <summary>Result of validating a future manually supplied Agent update artifact.</summary>
public sealed record AgentUpdatePreparationResponse(
    int ContractVersion,
    string CurrentVersion,
    string TargetVersion,
    bool IsDowngrade,
    string ArtifactDigest,
    bool RequiresManualInstallation,
    bool AutomaticApplySupported);
