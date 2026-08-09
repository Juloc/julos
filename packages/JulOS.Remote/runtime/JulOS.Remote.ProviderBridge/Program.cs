using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Remote.Transport;

const int JsonSecretKeyBytes = 16;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: JulOS.Remote.ProviderBridge <generate-key|finalize> <key-file> [nginx-token-file]");
    return 64;
}

var command = args[0];
var keyFilePath = args[1];

try
{
    switch (command)
    {
        case "generate-key":
            GenerateKey(keyFilePath);
            return 0;
        case "finalize":
            if (args.Length < 3)
            {
                Console.Error.WriteLine("The finalize command requires an nginx token output path.");
                return 64;
            }

            await FinalizeAsync(keyFilePath, args[2]).ConfigureAwait(false);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command '{command}'.");
            return 64;
    }
}
catch (ProviderBridgeException exception)
{
    Console.Error.WriteLine(exception.Message);
    await TryReportFailureAsync(exception.Code, exception.Message).ConfigureAwait(false);
    return 1;
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine(exception.Message);
    await TryReportFailureAsync("remote.provider_callback_unavailable", "The Remote provider could not reach its callback endpoint.").ConfigureAwait(false);
    return 1;
}

static void GenerateKey(string keyFilePath)
{
    var key = RandomNumberGenerator.GetBytes(JsonSecretKeyBytes);
    File.WriteAllText(keyFilePath, Convert.ToHexString(key).ToLowerInvariant());
}

static async Task FinalizeAsync(string keyFilePath, string nginxTokenFilePath)
{
    var keyHex = File.ReadAllText(keyFilePath).Trim();
    byte[] jsonSecretKey;
    try
    {
        jsonSecretKey = Convert.FromHexString(keyHex);
    }
    catch (FormatException exception)
    {
        throw new ProviderBridgeException("remote.provider_key_invalid", "The Guacamole JSON secret key file is invalid.", exception);
    }

    var sessionId = RequireEnvironment("JULOS_REMOTE_SESSION_ID");
    var protocol = RequireEnvironment("JULOS_REMOTE_PROTOCOL");
    var targetHost = RequireEnvironment("JULOS_REMOTE_TARGET_HOST");
    var targetPort = RequireInt("JULOS_REMOTE_TARGET_PORT");
    var maximumSessionSeconds = RequireInt("JULOS_REMOTE_MAXIMUM_SESSION_SECONDS");
    var credentialBase64 = RequireEnvironment("JULOS_REMOTE_TARGET_CREDENTIAL");

    var credential = ParseCredential(credentialBase64, protocol);
    var request = BuildLaunchRequest(sessionId, protocol, targetHost, targetPort, maximumSessionSeconds, credential);

    GuacamoleLaunchToken token;
    try
    {
        token = new GuacamoleJsonLaunchEncoder().Encode(request, jsonSecretKey);
    }
    catch (ArgumentException exception)
    {
        throw new ProviderBridgeException(
            "remote.provider_credential_invalid",
            "The Remote target credential could not be encoded for the provider.",
            exception);
    }

    // nginx cannot base64url-decode at proxy time, so the token is embedded directly
    // as a fixed upstream query string; the browser-facing listener never sees it.
    var tokenLine = $"set $julos_guac_token \"{token.EncryptedData}\";" + Environment.NewLine;
    File.WriteAllText(nginxTokenFilePath, tokenLine);

    await ReportConnectedAsync(sessionId).ConfigureAwait(false);
}

static ProviderCredential ParseCredential(string credentialBase64, string protocol)
{
    byte[] credentialBytes;
    try
    {
        credentialBytes = Convert.FromBase64String(credentialBase64);
    }
    catch (FormatException exception)
    {
        throw new ProviderBridgeException(
            "remote.provider_credential_invalid",
            "The Remote target credential is not valid Base64.",
            exception);
    }

    try
    {
        try
        {
            using var document = JsonDocument.Parse(credentialBytes);
            var root = document.RootElement;
            return new ProviderCredential(
                ReadOptionalString(root, "username"),
                ReadOptionalString(root, "password"),
                ReadOptionalString(root, "domain"),
                ReadOptionalString(root, "privateKey"),
                ReadOptionalString(root, "passphrase"));
        }
        catch (JsonException exception) when (string.Equals(protocol, RemoteTransportProtocols.Vnc, StringComparison.Ordinal))
        {
            string password;
            try
            {
                password = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                    .GetString(credentialBytes);
            }
            catch (DecoderFallbackException decoderException)
            {
                throw new ProviderBridgeException(
                    "remote.provider_credential_invalid",
                    "The VNC target credential is not valid UTF-8.",
                    decoderException);
            }

            if (string.IsNullOrWhiteSpace(password)
                || password.Length > 4096
                || password.Any(char.IsControl))
            {
                throw new ProviderBridgeException(
                    "remote.provider_credential_invalid",
                    "The VNC target credential is invalid.",
                    exception);
            }

            return new ProviderCredential(null, password, null, null, null);
        }
        catch (JsonException exception)
        {
            throw new ProviderBridgeException(
                "remote.provider_credential_invalid",
                "The Remote target credential is not valid JSON.",
                exception);
        }
    }
    finally
    {
        CryptographicOperations.ZeroMemory(credentialBytes);
    }
}

static string? ReadOptionalString(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;

static GuacamoleLaunchRequest BuildLaunchRequest(
    string sessionId,
    string protocol,
    string targetHost,
    int targetPort,
    int maximumSessionSeconds,
    ProviderCredential credential)
{
    var expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(maximumSessionSeconds, 1));
    var request = new GuacamoleLaunchRequest(
        CallerName: "julos",
        ConnectionName: sessionId,
        SessionId: sessionId,
        Protocol: protocol,
        Host: targetHost,
        Port: targetPort,
        UserName: credential.UserName,
        PasswordUtf8: credential.Password is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(credential.Password),
        Domain: string.Equals(protocol, RemoteTransportProtocols.Rdp, StringComparison.Ordinal) ? credential.Domain : null,
        IgnoreCertificate: false,
        KeyboardLayout: null,
        TerminalFontSize: 12,
        EnableDrive: false,
        DriveName: null,
        DrivePath: null,
        ClientName: "julos",
        ExpiresAtUtc: expiresAtUtc);

    if (string.Equals(protocol, RemoteTransportProtocols.Rdp, StringComparison.Ordinal))
    {
        return request with
        {
            RdpOptions = new GuacamoleRdpOptions(
                GuacamoleRdpSecurityModes.Any,
                GuacamoleRdpCertificatePolicies.Ignore,
                CertificateFingerprints: [],
                GuacamoleRdpResizeMethods.Reconnect,
                GuacamoleRdpClipboardPolicies.Bidirectional),
        };
    }

    if (string.Equals(protocol, RemoteTransportProtocols.Ssh, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(credential.PrivateKey))
    {
        return request with
        {
            SshOptions = new GuacamoleSshOptions(
                GuacamoleSshAuthenticationModes.PublicKey,
                GuacamoleSshHostKeyPolicies.Disabled,
                HostKey: null,
                PrivateKeyUtf8: Encoding.UTF8.GetBytes(credential.PrivateKey),
                PassphraseUtf8: credential.Passphrase is null
                    ? ReadOnlyMemory<byte>.Empty
                    : Encoding.UTF8.GetBytes(credential.Passphrase),
                TerminalFontName: "monospace",
                TerminalFontSize: 12,
                TimeoutSeconds: 20,
                ServerAliveIntervalSeconds: 0),
        };
    }

    return request;
}

static async Task ReportConnectedAsync(string sessionId)
{
    var callbackEndpoint = RequireEnvironment("JULOS_REMOTE_CALLBACK_ENDPOINT");
    var callbackToken = RequireEnvironment("JULOS_REMOTE_CALLBACK_TOKEN");
    var expectedRevision = RequireInt("JULOS_REMOTE_EXPECTED_REVISION");
    var runtimeId = $"remote-{Guid.Parse(sessionId):N}";

    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("X-JulOS-Remote-Token", callbackToken);
    var response = await client.PostAsJsonAsync(
        callbackEndpoint,
        new
        {
            sessionId,
            runtimeId,
            @event = "connected",
            expectedRevision,
            failureCode = (string?)null,
            failureDetail = (string?)null,
            retryable = false,
        }).ConfigureAwait(false);

    if (!response.IsSuccessStatusCode)
    {
        throw new ProviderBridgeException(
            "remote.provider_callback_failed",
            $"The Remote provider connected callback failed with status {(int)response.StatusCode}.");
    }
}

static async Task TryReportFailureAsync(string code, string detail)
{
    try
    {
        var callbackEndpoint = Environment.GetEnvironmentVariable("JULOS_REMOTE_CALLBACK_ENDPOINT");
        var callbackToken = Environment.GetEnvironmentVariable("JULOS_REMOTE_CALLBACK_TOKEN");
        var sessionId = Environment.GetEnvironmentVariable("JULOS_REMOTE_SESSION_ID");
        var expectedRevisionText = Environment.GetEnvironmentVariable("JULOS_REMOTE_EXPECTED_REVISION");
        if (callbackEndpoint is null || callbackToken is null || sessionId is null
            || !int.TryParse(expectedRevisionText, CultureInfo.InvariantCulture, out var expectedRevision))
        {
            return;
        }

        var runtimeId = $"remote-{Guid.Parse(sessionId):N}";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-JulOS-Remote-Token", callbackToken);
        _ = await client.PostAsJsonAsync(
            callbackEndpoint,
            new
            {
                sessionId,
                runtimeId,
                @event = "failed",
                expectedRevision,
                failureCode = code,
                failureDetail = detail,
                retryable = true,
            }).ConfigureAwait(false);
    }
    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or FormatException)
    {
        // Best-effort failure reporting; the container exits non-zero regardless.
    }
}

static string RequireEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name)
        ?? throw new ProviderBridgeException(
            "remote.provider_environment_missing",
            $"Required environment variable '{name}' is missing.");

static int RequireInt(string name)
{
    var value = RequireEnvironment(name);
    return int.TryParse(value, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : throw new ProviderBridgeException(
            "remote.provider_environment_invalid",
            $"Environment variable '{name}' is not a valid integer.");
}

internal sealed record ProviderCredential(
    string? UserName,
    string? Password,
    string? Domain,
    string? PrivateKey,
    string? Passphrase);

internal sealed class ProviderBridgeException : Exception
{
    public ProviderBridgeException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    public ProviderBridgeException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        this.Code = code;
    }

    public string Code { get; }
}
