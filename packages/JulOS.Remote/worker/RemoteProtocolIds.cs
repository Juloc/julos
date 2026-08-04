namespace JulOS.Remote.Worker;

/// <summary>Concrete protocol identities owned by the Remote package.</summary>
public static class RemoteProtocolIds
{
    /// <summary>Remote Desktop Protocol identity.</summary>
    public const string Rdp = "rdp";

    /// <summary>Virtual Network Computing protocol identity.</summary>
    public const string Vnc = "vnc";

    /// <summary>Secure Shell protocol identity.</summary>
    public const string Ssh = "ssh";

    /// <summary>Returns the conventional port used as a UI default.</summary>
    /// <param name="protocol">One package-owned protocol identity.</param>
    /// <returns>The conventional port.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The protocol is unknown to this package.</exception>
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
