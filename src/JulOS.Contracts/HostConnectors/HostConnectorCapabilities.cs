namespace JulOS.Contracts.HostConnectors;

/// <summary>
/// The Host-Connector-owned capability identities from <c>docs/HOST_CONNECTOR.md</c>
/// section 6, and the closed idempotency classification every dispatched request declares.
/// </summary>
/// <remarks>
/// <para>
/// Only the capabilities the Host Connector owns itself are named here. The adapter
/// capability families listed beside them in section 6 belong to the packages that own
/// those products and are declared in their own work items; Core stays free of
/// product-specific names, which <c>ProductTerminologyTests</c> enforces.
/// </para>
/// <para>
/// A capability is a typed, closed contract. A generic command, a shell invocation and an
/// arbitrary network destination are prohibited by decision, not merely absent.
/// </para>
/// </remarks>
public static class HostConnectorCapabilities
{
    /// <summary>Bounded Linux host observations, owned by Host Connector.</summary>
    public const string HostMetricsLinux = "host.metrics.linux";

    /// <summary>Sanitized version, reconnect and capability snapshot.</summary>
    public const string Diagnostics = "host.connector.diagnostics";

    /// <summary>Multiplexed stream bound to an already-authorized typed parent request.</summary>
    public const string HostStream = "host.stream";

    /// <summary>Every capability the Host Connector itself owns.</summary>
    public static IReadOnlyList<string> All { get; } = [HostMetricsLinux, Diagnostics, HostStream];

    /// <summary>
    /// Capability shapes that must never exist, whatever a future adapter proposes.
    /// </summary>
    /// <remarks>
    /// Kept as data so the contract validator can assert their absence instead of relying
    /// on review. See <c>docs/HOST_CONNECTOR.md</c> sections 6 and 11.
    /// </remarks>
    public static IReadOnlyList<string> Prohibited { get; } =
    [
        "host.command",
        "shell.execute",
        "host.shell",
        "tcp.proxy",
    ];
}

/// <summary>How a dispatched request may be recovered after an uncertain interruption.</summary>
public static class HostConnectorIdempotency
{
    /// <summary>
    /// Redelivery with the same Request ID before the deadline is safe, so an interrupted
    /// execution may run again.
    /// </summary>
    public const string ReplaySafe = "replay-safe";

    /// <summary>
    /// The external effect may already have happened, so the request is never redelivered
    /// for execution. The Connector reports <see cref="HostConnectorErrorCodes.OutcomeUnknown"/>
    /// and a new typed read-only request establishes the real state.
    /// </summary>
    public const string ReconcileRequired = "reconcile-required";

    /// <summary>Both accepted classifications.</summary>
    public static IReadOnlyList<string> All { get; } = [ReplaySafe, ReconcileRequired];
}
