namespace JulOS.Remote.Transport;

/// <summary>VNC display resize policy identities.</summary>
public static class GuacamoleVncResizePolicies
{
    /// <summary>Allows Guacamole to request display-size updates from the VNC server.</summary>
    public const string Dynamic = "dynamic";

    /// <summary>Keeps the VNC server display size fixed.</summary>
    public const string Fixed = "fixed";

    /// <summary>Returns whether one exact resize-policy identity is supported.</summary>
    public static bool IsSupported(string value) => value is Dynamic or Fixed;
}

/// <summary>VNC clipboard direction policy identities.</summary>
public static class GuacamoleVncClipboardPolicies
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

/// <summary>Apache Guacamole 1.6.0 VNC cursor-mode identities.</summary>
public static class GuacamoleVncCursorModes
{
    /// <summary>Uses the browser-side local cursor.</summary>
    public const string Local = "local";

    /// <summary>Renders the cursor on the remote display.</summary>
    public const string Remote = "remote";

    /// <summary>Returns whether one exact cursor-mode identity is supported.</summary>
    public static bool IsSupported(string value) => value is Local or Remote;
}

/// <summary>Apache Guacamole 1.6.0 VNC clipboard encodings.</summary>
public static class GuacamoleVncClipboardEncodings
{
    /// <summary>Uses the VNC-standard ISO 8859-1 encoding.</summary>
    public const string Iso88591 = "ISO8859-1";

    /// <summary>Uses UTF-8 when the target VNC server supports it.</summary>
    public const string Utf8 = "UTF-8";

    /// <summary>Uses UTF-16 when the target VNC server supports it.</summary>
    public const string Utf16 = "UTF-16";

    /// <summary>Uses Windows code page 1252.</summary>
    public const string Windows1252 = "CP1252";

    /// <summary>Returns whether one exact clipboard encoding is supported.</summary>
    public static bool IsSupported(string value) => value is
        Iso88591
        or Utf8
        or Utf16
        or Windows1252;
}

/// <summary>Explicit provider-side VNC options for Apache Guacamole 1.6.0.</summary>
/// <param name="ResizePolicy">One value from <see cref="GuacamoleVncResizePolicies"/>.</param>
/// <param name="ClipboardPolicy">One value from <see cref="GuacamoleVncClipboardPolicies"/>.</param>
/// <param name="CursorMode">One value from <see cref="GuacamoleVncCursorModes"/>.</param>
/// <param name="ClipboardEncoding">Optional value from <see cref="GuacamoleVncClipboardEncodings"/>.</param>
/// <param name="ColorDepth">Optional color depth: 8, 16, 24 or 32 bits.</param>
/// <param name="AutoRetry">Optional bounded connection retry count from 0 through 10.</param>
/// <param name="CompressionLevel">Optional compression level from 0 through 9.</param>
/// <param name="QualityLevel">Optional JPEG quality level from 0 through 9.</param>
/// <param name="ReadOnly">Whether all remote input is disabled.</param>
/// <param name="DisableServerInput">Whether the VNC server should disable its local input devices.</param>
/// <param name="SwapRedBlue">Whether red and blue display channels are swapped.</param>
/// <param name="ForceLossless">Whether graphical updates use lossless compression only.</param>
public sealed record GuacamoleVncOptions(
    string ResizePolicy,
    string ClipboardPolicy,
    string CursorMode,
    string? ClipboardEncoding,
    int? ColorDepth,
    int? AutoRetry,
    int? CompressionLevel,
    int? QualityLevel,
    bool ReadOnly,
    bool DisableServerInput,
    bool SwapRedBlue,
    bool ForceLossless);
