namespace JulOS.Remote.Transport;

/// <summary>Concrete protocol identities implemented by the JulOS 1.0 Remote transport.</summary>
public static class RemoteTransportProtocols
{
    private static readonly string[] Protocols = [Rdp, Vnc, Ssh];

    /// <summary>Remote Desktop Protocol identity.</summary>
    public const string Rdp = "rdp";

    /// <summary>Virtual Network Computing protocol identity.</summary>
    public const string Vnc = "vnc";

    /// <summary>Secure Shell protocol identity.</summary>
    public const string Ssh = "ssh";

    /// <summary>Gets the supported protocol identities in stable display order.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Protocols);

    /// <summary>Returns whether one exact lowercase protocol identity is supported.</summary>
    /// <param name="protocol">Protocol identity.</param>
    /// <returns>Whether the identity is supported.</returns>
    public static bool IsSupported(string protocol) =>
        string.Equals(protocol, Rdp, StringComparison.Ordinal)
        || string.Equals(protocol, Vnc, StringComparison.Ordinal)
        || string.Equals(protocol, Ssh, StringComparison.Ordinal);

    /// <summary>Returns the conventional port used as a UI default.</summary>
    /// <param name="protocol">One supported protocol identity.</param>
    /// <returns>The conventional protocol port.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The protocol is unsupported.</exception>
    public static int DefaultPort(string protocol) => protocol switch
    {
        Rdp => 3389,
        Vnc => 5900,
        Ssh => 22,
        _ => throw new ArgumentOutOfRangeException(
            nameof(protocol),
            protocol,
            "Remote protocol is unsupported."),
    };
}
