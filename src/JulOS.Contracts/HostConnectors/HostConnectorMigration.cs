namespace JulOS.Contracts.HostConnectors;

/// <summary>
/// The constants and mappings <c>HCON-002</c> must apply when the legacy Agent becomes the
/// Host Connector, from <c>docs/HOST_CONNECTOR.md</c> section 10.
/// </summary>
/// <remarks>
/// `HCON-001` commits this map so the rename is reviewable before any code moves. Nothing
/// here is a compatibility adapter: after the cutover the old runtime route exists for at
/// most one announced transition release as a non-executing <c>426</c> tombstone.
/// </remarks>
public static class HostConnectorMigration
{
    /// <summary>
    /// The historical machine-identity hash namespace, retained exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is data compatibility, not a public Agent path. An already-enrolled machine
    /// must keep its identity across the rename, so the literal <c>JulOS.Agent\0</c> prefix
    /// is never localized, renamed or re-cased. Changing it requires a new identity version
    /// and an explicit server-side association flow, never a silent re-enrollment.
    /// </para>
    /// <para>
    /// <c>MachineIdentityV1 = lowercase-hex(SHA256(MachineIdentityNamespaceV1 || UTF8(trimmed source)))</c>.
    /// </para>
    /// </remarks>
    public const string MachineIdentityNamespaceV1 = "JulOS.Agent\0";

    /// <summary>Default Linux identity file of the legacy Agent.</summary>
    public const string LegacyIdentityPathLinux = "/var/lib/julos-agent/identity.json";

    /// <summary>Default Linux identity file of the Host Connector.</summary>
    public const string HostConnectorIdentityPathLinux = "/var/lib/julos-host-connector/identity.json";

    /// <summary>Default Linux request-journal directory.</summary>
    public const string RequestJournalPathLinux = "/var/lib/julos-host-connector/request-journal";

    /// <summary>Default Windows request-journal directory.</summary>
    public const string RequestJournalPathWindows = @"%ProgramData%\JulOS\HostConnector\request-journal";

    /// <summary>Environment variable that may replace the journal directory with an absolute path.</summary>
    public const string RequestJournalPathVariable = "JULOS_HOST_CONNECTOR_JOURNAL_PATH";

    /// <summary>Bounded pre-cutover drain command that retires queued legacy requests.</summary>
    public const string DrainCommandSwitch = "--drain-legacy-agent-requests";

    /// <summary>Failure code written to legacy rows the drain cancels.</summary>
    public const string DrainCancellationCode = "agent.command_cancelled_for_upgrade";

    /// <summary>Table renames applied without drop and recreate.</summary>
    /// <remarks>
    /// <c>agent_commands</c> is the one entry that is not a Host Connector table: its rows
    /// move to read-only retention and are never copied into the typed request table.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> TableRenames { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agents"] = "host_connectors",
            ["agent_capabilities"] = "host_connector_capabilities",
            ["agent_credentials"] = "host_connector_credentials",
            ["agent_enrollment_tokens"] = "host_connector_enrollment_tokens",
            ["agent_commands"] = "legacy_agent_commands",
            ["agent_metric_samples"] = "host_connector_metric_samples",
        };

    /// <summary>Column renames applied without drop and recreate.</summary>
    public static IReadOnlyDictionary<string, string> ColumnRenames { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agent_id"] = "host_connector_id",
            ["redeemed_by_agent_id"] = "redeemed_by_host_connector_id",
            ["name"] = "display_name",
        };

    /// <summary>Tables created by the migration rather than renamed into.</summary>
    public static IReadOnlyList<string> NewTables { get; } =
    [
        "host_connector_requests",
        "host_connector_credential_rotations",
    ];

    /// <summary>Namespace renames applied to the source tree.</summary>
    public static IReadOnlyDictionary<string, string> NamespaceRenames { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JulOS.Agent"] = "JulOS.HostConnector",
            ["JulOS.Domain.Agents"] = "JulOS.Domain.HostConnectors",
        };

    /// <summary>
    /// Permission assignments copied forward, keyed by the existing global permission.
    /// </summary>
    /// <remarks>
    /// Only global assignments are copied. Package- or resource-scoped assignments are
    /// never widened, and no right is inferred from the fact that a Connector enrolled.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PermissionMigration { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["core.authorization.read"] = [HostConnectorPermissions.Read],
            ["core.authorization.manage"] =
            [
                HostConnectorPermissions.Read,
                HostConnectorPermissions.Manage,
                HostConnectorPermissions.Diagnostics,
            ],
        };
}
