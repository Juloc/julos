namespace JulOS.Contracts.Packages;

/// <summary>Stable identity and bounds for the JulOS 1.0 Host Metrics capability.</summary>
public static class HostMetricsCapabilityContract
{
    /// <summary>Capability identity.</summary>
    public const string Name = "host.metrics.read";

    /// <summary>Capability contract version.</summary>
    public const string Version = "1.0.0";

    /// <summary>Operation returning the latest persisted observations.</summary>
    public const string LatestOperation = "latest";

    /// <summary>Default freshness threshold.</summary>
    public const int DefaultMaximumAgeSeconds = 90;

    /// <summary>Minimum accepted freshness threshold.</summary>
    public const int MinimumMaximumAgeSeconds = 15;

    /// <summary>Maximum accepted freshness threshold.</summary>
    public const int MaximumMaximumAgeSeconds = 900;
}

/// <summary>Stable Host Metrics snapshot state names.</summary>
public static class HostMetricsSnapshotStates
{
    /// <summary>The Agent and latest observations are current.</summary>
    public const string Live = "live";

    /// <summary>The Agent is current but the latest observations are old.</summary>
    public const string Stale = "stale";

    /// <summary>The Agent is not currently connected.</summary>
    public const string Offline = "offline";

    /// <summary>The Agent is connected but no observations are available.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>Requests the latest metrics for one Agent or the single available Agent.</summary>
/// <param name="AgentId">Optional explicit Agent identity.</param>
/// <param name="MaximumAgeSeconds">Optional freshness threshold.</param>
public sealed record HostMetricsReadRequest(
    Guid? AgentId,
    int? MaximumAgeSeconds);

/// <summary>One latest metric value without replacing unknown values with zero.</summary>
/// <param name="Name">Metric identity.</param>
/// <param name="Value">Latest value, or unknown.</param>
/// <param name="Unit">Metric unit.</param>
/// <param name="Labels">Series labels.</param>
/// <param name="ObservedAtUtc">Original Agent observation time.</param>
public sealed record HostMetricValueResponse(
    string Name,
    double? Value,
    string Unit,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset ObservedAtUtc);

/// <summary>Latest bounded Host Metrics view returned to an authorized package.</summary>
/// <param name="AgentId">Resolved Agent identity, or none when no Agent is enrolled.</param>
/// <param name="State">Live, stale, offline or unavailable state.</param>
/// <param name="Stale">Whether observations exceeded the requested freshness threshold.</param>
/// <param name="ObservedAtUtc">Newest metric observation time.</param>
/// <param name="Metrics">Latest values for persisted supported series.</param>
public sealed record HostMetricsSnapshotResponse(
    Guid? AgentId,
    string State,
    bool Stale,
    DateTimeOffset? ObservedAtUtc,
    IReadOnlyList<HostMetricValueResponse> Metrics);
