namespace JulOS.Remote.Transport;

/// <summary>Apache Guacamole 1.6.0 RDP security-mode identities.</summary>
public static class GuacamoleRdpSecurityModes
{
    /// <summary>Negotiates the strongest mutually supported security mode.</summary>
    public const string Any = "any";

    /// <summary>Uses Network Level Authentication.</summary>
    public const string NetworkLevelAuthentication = "nla";

    /// <summary>Uses extended Network Level Authentication.</summary>
    public const string ExtendedNetworkLevelAuthentication = "nla-ext";

    /// <summary>Uses RDSTLS.</summary>
    public const string Tls = "tls";

    /// <summary>Uses the Hyper-V VMConnect-compatible negotiation set.</summary>
    public const string VmConnect = "vmconnect";

    /// <summary>Uses legacy RDP encryption.</summary>
    public const string LegacyRdp = "rdp";

    /// <summary>Returns whether one exact security-mode identity is supported.</summary>
    public static bool IsSupported(string value) => value is
        Any
        or NetworkLevelAuthentication
        or ExtendedNetworkLevelAuthentication
        or Tls
        or VmConnect
        or LegacyRdp;

    /// <summary>Returns whether the security mode requires credentials before connection.</summary>
    public static bool RequiresPreConnectionCredentials(string value) => value is
        NetworkLevelAuthentication
        or ExtendedNetworkLevelAuthentication;
}

/// <summary>Caller-selected RDP certificate policy identities.</summary>
public static class GuacamoleRdpCertificatePolicies
{
    /// <summary>Requires normal certificate validation.</summary>
    public const string Strict = "strict";

    /// <summary>Ignores certificate validation failures.</summary>
    public const string Ignore = "ignore";

    /// <summary>Trusts the first certificate and requires the same certificate later.</summary>
    public const string TrustOnFirstUse = "tofu";

    /// <summary>Accepts only an explicitly configured certificate fingerprint.</summary>
    public const string Pinned = "pinned";

    /// <summary>Returns whether one exact certificate-policy identity is supported.</summary>
    public static bool IsSupported(string value) => value is
        Strict
        or Ignore
        or TrustOnFirstUse
        or Pinned;
}

/// <summary>Apache Guacamole 1.6.0 RDP resize-method identities.</summary>
public static class GuacamoleRdpResizeMethods
{
    /// <summary>Uses the RDP 8.1 Display Update channel.</summary>
    public const string DisplayUpdate = "display-update";

    /// <summary>Reconnects the RDP session with the new size.</summary>
    public const string Reconnect = "reconnect";

    /// <summary>Returns whether one exact resize-method identity is supported.</summary>
    public static bool IsSupported(string value) => value is DisplayUpdate or Reconnect;
}

/// <summary>RDP clipboard direction policy identities.</summary>
public static class GuacamoleRdpClipboardPolicies
{
    /// <summary>Allows copy and paste in both directions.</summary>
    public const string Bidirectional = "bidirectional";

    /// <summary>Allows browser clipboard data to be pasted into the remote session only.</summary>
    public const string BrowserToRemote = "browser-to-remote";

    /// <summary>Allows remote clipboard data to be copied to the browser only.</summary>
    public const string RemoteToBrowser = "remote-to-browser";

    /// <summary>Disables clipboard transfer in both directions.</summary>
    public const string Disabled = "disabled";

    /// <summary>Returns whether one exact clipboard-policy identity is supported.</summary>
    public static bool IsSupported(string value) => value is
        Bidirectional
        or BrowserToRemote
        or RemoteToBrowser
        or Disabled;
}

/// <summary>Explicit provider-side RDP options for Apache Guacamole 1.6.0.</summary>
/// <param name="SecurityMode">One value from <see cref="GuacamoleRdpSecurityModes"/>.</param>
/// <param name="CertificatePolicy">One value from <see cref="GuacamoleRdpCertificatePolicies"/>.</param>
/// <param name="CertificateFingerprints">Certificate hashes used only with the pinned policy.</param>
/// <param name="ResizeMethod">One value from <see cref="GuacamoleRdpResizeMethods"/>.</param>
/// <param name="ClipboardPolicy">One value from <see cref="GuacamoleRdpClipboardPolicies"/>.</param>
public sealed record GuacamoleRdpOptions(
    string SecurityMode,
    string CertificatePolicy,
    IReadOnlyList<string> CertificateFingerprints,
    string ResizeMethod,
    string ClipboardPolicy)
{
    /// <summary>Creates the previous transport behavior for consumers that have not adopted explicit policy.</summary>
    internal static GuacamoleRdpOptions CompatibilityDefaults(bool ignoreCertificate) =>
        new(
            GuacamoleRdpSecurityModes.Any,
            ignoreCertificate
                ? GuacamoleRdpCertificatePolicies.Ignore
                : GuacamoleRdpCertificatePolicies.Strict,
            Array.Empty<string>(),
            GuacamoleRdpResizeMethods.Reconnect,
            GuacamoleRdpClipboardPolicies.Bidirectional);
}
