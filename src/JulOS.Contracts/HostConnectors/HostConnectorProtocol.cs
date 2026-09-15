namespace JulOS.Contracts.HostConnectors;

/// <summary>
/// The canonical Host Connector protocol identities from <c>docs/HOST_CONNECTOR.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// `HCON-001` locks these values before `HCON-002` renames the legacy Agent implementation
/// onto them. Nothing here executes: the constants exist so the contract fixtures, the
/// migration map and the eventual implementation cannot drift from the specification or
/// from each other.
/// </para>
/// <para>
/// No value in this namespace describes a generic command, a shell invocation, a raw TCP
/// destination or a product-specific control payload. Capability requests are typed and
/// closed; see <see cref="HostConnectorCapabilities"/>.
/// </para>
/// </remarks>
public static class HostConnectorProtocol
{
    /// <summary>The only accepted runtime protocol version.</summary>
    public const int Version = 1;

    /// <summary>Lowest protocol version this release accepts.</summary>
    public const int MinimumVersion = 1;

    /// <summary>Highest protocol version this release accepts.</summary>
    public const int MaximumVersion = 1;

    /// <summary>Request header carrying the Connector's protocol version.</summary>
    public const string ProtocolHeaderName = "X-JulOS-Host-Connector-Protocol";

    /// <summary>Response header carrying the lowest protocol version Server accepts.</summary>
    public const string ProtocolMinimumHeaderName = "X-JulOS-Host-Connector-Protocol-Min";

    /// <summary>Response header carrying the highest protocol version Server accepts.</summary>
    public const string ProtocolMaximumHeaderName = "X-JulOS-Host-Connector-Protocol-Max";

    /// <summary>Request header carrying the enrolled Host Connector identity.</summary>
    public const string HostConnectorIdHeaderName = "X-JulOS-Host-Connector-Id";

    /// <summary>Prefix shared by every runtime endpoint.</summary>
    public const string RuntimePrefix = "/api/v1/host-connector";

    /// <summary>Prefix shared by every browser-facing administration endpoint.</summary>
    public const string AdministrationPrefix = "/api/v1/host-connectors";

    /// <summary>Lowest accepted long-poll wait, in seconds.</summary>
    public const int MinimumPollWaitSeconds = 1;

    /// <summary>Highest accepted long-poll wait, in seconds.</summary>
    public const int MaximumPollWaitSeconds = 30;

    /// <summary>Length in bytes of the random material behind a <c>CredentialV1</c>.</summary>
    public const int CredentialByteLength = 48;

    /// <summary>How long an unattempted credential rotation stays requested.</summary>
    public static readonly TimeSpan CredentialRotationRequestLifetime = TimeSpan.FromMinutes(15);

    /// <summary>How long both the old and the pending credential authenticate during overlap.</summary>
    public static readonly TimeSpan CredentialRotationOverlap = TimeSpan.FromMinutes(10);

    /// <summary>The fixed runtime endpoints.</summary>
    /// <remarks>
    /// The set is closed. A new Connector behaviour is a new typed capability, never a new
    /// free-form endpoint.
    /// </remarks>
    public static class Runtime
    {
        /// <summary>Redeems a one-time enrollment token.</summary>
        public const string Enrollment = RuntimePrefix + "/enrollment";

        /// <summary>Reports liveness and negotiated state.</summary>
        public const string Heartbeat = RuntimePrefix + "/heartbeat";

        /// <summary>Submits bounded host-metric observations.</summary>
        public const string HostMetricObservations = RuntimePrefix + "/observations/host-metrics";

        /// <summary>Bounded long-poll for the next dispatched request.</summary>
        public const string NextRequest = RuntimePrefix + "/requests/next";

        /// <summary>Submits the typed result of one claimed request.</summary>
        public const string RequestResultTemplate = RuntimePrefix + "/requests/{requestId}/result";

        /// <summary>Submits a newly generated credential during rotation.</summary>
        public const string CredentialRotationAttemptTemplate =
            RuntimePrefix + "/credential-rotations/{rotationId}/attempt";

        /// <summary>Commits a prepared credential rotation.</summary>
        public const string CredentialRotationAcknowledgementTemplate =
            RuntimePrefix + "/credential-rotations/{rotationId}/acknowledgement";
    }

    /// <summary>The browser-facing administration endpoints.</summary>
    public static class Administration
    {
        /// <summary>Creates a single-use enrollment token.</summary>
        public const string EnrollmentTokens = AdministrationPrefix + "/enrollment-tokens";

        /// <summary>Lists enrolled Host Connectors.</summary>
        public const string Collection = AdministrationPrefix;

        /// <summary>Reads or renames one Host Connector.</summary>
        public const string ItemTemplate = AdministrationPrefix + "/{hostConnectorId}";

        /// <summary>Terminally revokes one Host Connector credential.</summary>
        public const string RevocationTemplate = AdministrationPrefix + "/{hostConnectorId}/revocation";

        /// <summary>Starts an administrator-initiated credential rotation.</summary>
        public const string CredentialRotationsTemplate =
            AdministrationPrefix + "/{hostConnectorId}/credential-rotations";

        /// <summary>Requests a sanitized diagnostics snapshot.</summary>
        public const string DiagnosticsTemplate = AdministrationPrefix + "/{hostConnectorId}/diagnostics";

        /// <summary>Lists the capabilities one Host Connector advertises.</summary>
        public const string CapabilitiesTemplate = AdministrationPrefix + "/{hostConnectorId}/capabilities";
    }

    /// <summary>Realtime event names.</summary>
    public static class Events
    {
        /// <summary>Replaces the legacy <c>agent.status.changed</c> event at a new contract version.</summary>
        public const string StatusChanged = "host_connector.status.changed";

        /// <summary>Contract version of <see cref="StatusChanged"/>.</summary>
        public const int StatusChangedContractVersion = 1;
    }
}
