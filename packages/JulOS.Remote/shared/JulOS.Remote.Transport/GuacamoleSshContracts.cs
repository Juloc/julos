namespace JulOS.Remote.Transport;

/// <summary>SSH authentication-mode identities.</summary>
public static class GuacamoleSshAuthenticationModes
{
    /// <summary>Uses the request password.</summary>
    public const string Password = "password";

    /// <summary>Uses an OpenSSH private key and optional passphrase.</summary>
    public const string PublicKey = "public-key";

    /// <summary>Uses the SSH NONE method with only a username.</summary>
    public const string None = "none";

    /// <summary>Returns whether one exact authentication-mode identity is supported.</summary>
    public static bool IsSupported(string value) => value is Password or PublicKey or None;
}

/// <summary>SSH host-key verification policy identities.</summary>
public static class GuacamoleSshHostKeyPolicies
{
    /// <summary>Requires one exact OpenSSH known-hosts entry.</summary>
    public const string Strict = "strict";

    /// <summary>Disables per-connection host-key verification.</summary>
    public const string Disabled = "disabled";

    /// <summary>Returns whether one exact host-key policy identity is supported.</summary>
    public static bool IsSupported(string value) => value is Strict or Disabled;
}

/// <summary>Explicit provider-side SSH options for Apache Guacamole 1.6.0.</summary>
/// <param name="AuthenticationMode">One value from <see cref="GuacamoleSshAuthenticationModes"/>.</param>
/// <param name="HostKeyPolicy">One value from <see cref="GuacamoleSshHostKeyPolicies"/>.</param>
/// <param name="HostKey">One exact OpenSSH known-hosts entry when strict verification is enabled.</param>
/// <param name="PrivateKeyUtf8">Optional OpenSSH private key encoded as UTF-8. The caller owns and clears the backing buffer.</param>
/// <param name="PassphraseUtf8">Optional private-key passphrase encoded as UTF-8. The caller owns and clears the backing buffer.</param>
/// <param name="TerminalFontName">Terminal font family.</param>
/// <param name="TerminalFontSize">Terminal font size from 8 through 24.</param>
/// <param name="TimeoutSeconds">SSH connection timeout from 1 through 120 seconds.</param>
/// <param name="ServerAliveIntervalSeconds">Keepalive interval: 0 to disable, otherwise 2 through 300 seconds.</param>
public sealed record GuacamoleSshOptions(
    string AuthenticationMode,
    string HostKeyPolicy,
    string? HostKey,
    ReadOnlyMemory<byte> PrivateKeyUtf8,
    ReadOnlyMemory<byte> PassphraseUtf8,
    string TerminalFontName,
    int TerminalFontSize,
    int TimeoutSeconds,
    int ServerAliveIntervalSeconds);
