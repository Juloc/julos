namespace JulOS.Contracts.HostConnectors;

/// <summary>
/// The Host Connector permissions from <c>docs/HOST_CONNECTOR.md</c> section 8.
/// </summary>
/// <remarks>
/// Package-owned rights stay separate: holding a Host
/// Connector permission never implies a package capability.
/// </remarks>
public static class HostConnectorPermissions
{
    /// <summary>Read enrolled Host Connectors and their capabilities.</summary>
    public const string Read = "host_connectors.read";

    /// <summary>Create enrollment tokens, rename, revoke and rotate credentials.</summary>
    public const string Manage = "host_connectors.manage";

    /// <summary>Request a sanitized diagnostics snapshot.</summary>
    public const string Diagnostics = "host_connectors.diagnostics";

    /// <summary>Every Host Connector permission.</summary>
    public static IReadOnlyList<string> All { get; } = [Read, Manage, Diagnostics];
}
