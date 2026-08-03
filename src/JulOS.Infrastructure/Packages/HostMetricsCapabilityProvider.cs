using System.Text.Json;

using JulOS.Application.Agents;
using JulOS.Contracts.Agents;
using JulOS.Contracts.Packages;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Packages;

/// <summary>Provides bounded latest host metrics from persisted Agent telemetry.</summary>
public sealed class HostMetricsCapabilityProvider : ICapabilityProvider
{
    /// <summary>Core-owned provider identity used by the capability broker.</summary>
    public const string ProviderPackageId = "julos.core.hostmetrics";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedMetricNames = new(
        [
            "host.cpu.utilization",
            "host.memory.total_bytes",
            "host.memory.used_bytes",
            "host.load.one",
            "host.load.five",
            "host.load.fifteen",
            "host.uptime.seconds",
            "host.disk.total_bytes",
            "host.disk.used_bytes",
            "host.network.receive_bytes_total",
            "host.network.transmit_bytes_total",
        ],
        StringComparer.Ordinal);

    private readonly IAgentControlService agents;
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the persisted Agent telemetry provider.</summary>
    /// <param name="agents">Agent control-plane application service.</param>
    /// <param name="timeProvider">Authoritative clock.</param>
    public HostMetricsCapabilityProvider(
        IAgentControlService agents,
        TimeProvider timeProvider)
    {
        this.agents = agents ?? throw new ArgumentNullException(nameof(agents));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public CapabilityProviderDescriptor Descriptor { get; } = new(
        ProviderPackageId,
        HostMetricsCapabilityContract.Name,
        HostMetricsCapabilityContract.Version,
        Priority: 1000,
        Healthy: true);

    /// <inheritdoc />
    public async Task<CapabilityResponse> InvokeAsync(
        CapabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.CapabilityName, HostMetricsCapabilityContract.Name, StringComparison.Ordinal)
            || !string.Equals(request.ContractVersion, HostMetricsCapabilityContract.Version, StringComparison.Ordinal))
        {
            return Failure(
                "hostmetrics.contract_incompatible",
                "The requested Host Metrics capability contract is incompatible.");
        }

        if (!string.Equals(request.Operation, HostMetricsCapabilityContract.LatestOperation, StringComparison.Ordinal))
        {
            return Failure(
                "hostmetrics.operation_unsupported",
                "The requested Host Metrics operation is not supported.");
        }

        HostMetricsReadRequest input;
        try
        {
            input = request.Payload.Deserialize<HostMetricsReadRequest>(JsonOptions)
                ?? new HostMetricsReadRequest(null, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return Failure(
                "hostmetrics.request_invalid",
                "The Host Metrics request payload is invalid.");
        }

        var maximumAgeSeconds = input.MaximumAgeSeconds
            ?? HostMetricsCapabilityContract.DefaultMaximumAgeSeconds;
        if (maximumAgeSeconds is < HostMetricsCapabilityContract.MinimumMaximumAgeSeconds
            or > HostMetricsCapabilityContract.MaximumMaximumAgeSeconds)
        {
            return Failure(
                "hostmetrics.maximum_age_invalid",
                "The Host Metrics maximum age is outside the supported range.");
        }

        AgentResponse? agent;
        try
        {
            agent = input.AgentId is null
                ? await this.ResolveDefaultAgentAsync(cancellationToken).ConfigureAwait(false)
                : await this.agents.ReadAsync(input.AgentId.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentControlException exception)
        {
            return Failure(exception.Code, exception.Message);
        }

        if (agent is null)
        {
            return Success(new HostMetricsSnapshotResponse(
                AgentId: null,
                HostMetricsSnapshotStates.Offline,
                Stale: false,
                ObservedAtUtc: null,
                Metrics: []));
        }

        var now = this.timeProvider.GetUtcNow();
        var rangeSeconds = Math.Max(maximumAgeSeconds * 4, 600);
        IReadOnlyList<AgentMetricSeriesResponse> series;
        try
        {
            series = await this.agents.ReadMetricsAsync(
                agent.AgentId,
                now.AddSeconds(-rangeSeconds),
                now,
                cancellationToken).ConfigureAwait(false);
        }
        catch (AgentControlException exception)
        {
            return Failure(exception.Code, exception.Message);
        }

        var metrics = LatestMetrics(series);
        var observedAt = metrics.Count == 0
            ? (DateTimeOffset?)null
            : metrics.Max(metric => metric.ObservedAtUtc);
        var cutoff = now.AddSeconds(-maximumAgeSeconds);
        var connected = string.Equals(agent.State, "connected", StringComparison.Ordinal)
            && agent.LastSeenAtUtc is not null
            && agent.LastSeenAtUtc.Value >= cutoff;

        var state = !connected
            ? HostMetricsSnapshotStates.Offline
            : metrics.Count == 0
                ? HostMetricsSnapshotStates.Unavailable
                : observedAt < cutoff
                    ? HostMetricsSnapshotStates.Stale
                    : HostMetricsSnapshotStates.Live;

        return Success(new HostMetricsSnapshotResponse(
            agent.AgentId,
            state,
            string.Equals(state, HostMetricsSnapshotStates.Stale, StringComparison.Ordinal),
            observedAt,
            metrics));
    }

    private async Task<AgentResponse?> ResolveDefaultAgentAsync(CancellationToken cancellationToken)
    {
        var available = (await this.agents.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(agent => !string.Equals(agent.State, "revoked", StringComparison.Ordinal))
            .ToArray();
        if (available.Length == 0)
        {
            return null;
        }

        var connected = available
            .Where(agent => string.Equals(agent.State, "connected", StringComparison.Ordinal))
            .ToArray();
        if (connected.Length == 1)
        {
            return connected[0];
        }

        if (available.Length == 1)
        {
            return available[0];
        }

        throw new AgentControlException(
            "hostmetrics.agent_required",
            "More than one Agent is available; a target Agent must be selected.");
    }

    private static List<HostMetricValueResponse> LatestMetrics(
        IReadOnlyList<AgentMetricSeriesResponse> series)
    {
        var metrics = new List<HostMetricValueResponse>();
        foreach (var item in series.OrderBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            if (!SupportedMetricNames.Contains(item.Name))
            {
                continue;
            }

            var point = item.Points
                .OrderByDescending(candidate => candidate.ObservedAtUtc)
                .FirstOrDefault();
            if (point is null)
            {
                continue;
            }

            metrics.Add(new HostMetricValueResponse(
                item.Name,
                point.Value,
                item.Unit,
                item.Labels,
                point.ObservedAtUtc));
        }

        return metrics;
    }

    private static CapabilityResponse Success(HostMetricsSnapshotResponse snapshot) => new(
        Succeeded: true,
        ErrorCode: null,
        ErrorDetail: null,
        JsonSerializer.SerializeToElement(snapshot, JsonOptions));

    private static CapabilityResponse Failure(string code, string detail) => new(
        Succeeded: false,
        code,
        detail,
        JsonSerializer.SerializeToElement(new { }, JsonOptions));
}
