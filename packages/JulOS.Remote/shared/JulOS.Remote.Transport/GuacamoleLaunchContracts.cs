namespace JulOS.Remote.Transport;

/// <summary>Provider-side input used to create one Guacamole JSON-auth launch token.</summary>
/// <param name="CallerName">Authenticated display caller name supplied to Guacamole.</param>
/// <param name="ConnectionName">Caller-visible connection name.</param>
/// <param name="SessionId">Unique provider session identity.</param>
/// <param name="Protocol">One identity from <see cref="RemoteTransportProtocols"/>.</param>
/// <param name="Host">Target DNS name or IP address.</param>
/// <param name="Port">Target port.</param>
/// <param name="UserName">Optional target user name.</param>
/// <param name="PasswordUtf8">Optional target password encoded as UTF-8. The caller owns and clears the backing buffer.</param>
/// <param name="Domain">Optional desktop domain.</param>
/// <param name="IgnoreCertificate">Whether the desktop provider may ignore the target certificate.</param>
/// <param name="KeyboardLayout">Optional desktop keyboard layout.</param>
/// <param name="TerminalFontSize">Requested terminal font size.</param>
/// <param name="EnableDrive">Whether desktop drive redirection is enabled.</param>
/// <param name="DriveName">Optional redirected drive name.</param>
/// <param name="DrivePath">Optional provider-local redirected drive path.</param>
/// <param name="ClientName">Optional provider client name reported to the target.</param>
/// <param name="ExpiresAtUtc">Absolute launch expiry.</param>
public sealed record GuacamoleLaunchRequest(
    string CallerName,
    string ConnectionName,
    string SessionId,
    string Protocol,
    string Host,
    int Port,
    string? UserName,
    ReadOnlyMemory<byte> PasswordUtf8,
    string? Domain,
    bool IgnoreCertificate,
    string? KeyboardLayout,
    int TerminalFontSize,
    bool EnableDrive,
    string? DriveName,
    string? DrivePath,
    string? ClientName,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Encrypted Guacamole launch data produced inside the provider boundary.</summary>
/// <param name="EncryptedData">Base64-encoded encrypted and authenticated JSON payload.</param>
/// <param name="ClientIdentifier">Base64-encoded Guacamole client identifier.</param>
/// <param name="ConnectionName">Connection name included in the payload.</param>
/// <param name="ExpiresAtUtc">Absolute launch expiry.</param>
public sealed record GuacamoleLaunchToken(
    string EncryptedData,
    string ClientIdentifier,
    string ConnectionName,
    DateTimeOffset ExpiresAtUtc);
