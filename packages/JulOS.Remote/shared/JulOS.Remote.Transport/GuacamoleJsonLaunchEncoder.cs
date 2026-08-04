using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JulOS.Remote.Transport;

/// <summary>Creates Guacamole JSON-auth launch data inside the provider trust boundary.</summary>
public sealed class GuacamoleJsonLaunchEncoder
{
    private const int JsonSecretKeyBytes = 16;
    private const int SignatureBytes = 32;
    private const string DefaultKeyboardLayout = "de-de-qwertz";
    private const int DefaultTerminalFontSize = 12;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Creates one encrypted and authenticated Guacamole launch token.</summary>
    /// <param name="request">Validated provider-side launch request.</param>
    /// <param name="jsonSecretKey">The 16-byte Guacamole JSON-auth key.</param>
    /// <returns>The encrypted launch token and client identifier.</returns>
    /// <exception cref="ArgumentException">The request or key is invalid.</exception>
    public GuacamoleLaunchToken Encode(
        GuacamoleLaunchRequest request,
        ReadOnlySpan<byte> jsonSecretKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request, jsonSecretKey);

        using var payloadStream = new MemoryStream();
        WritePayload(payloadStream, request);

        var payloadLength = checked((int)payloadStream.Length);
        var payloadBuffer = payloadStream.GetBuffer();
        var payload = payloadBuffer.AsSpan(0, payloadLength);
        var signature = HMACSHA256.HashData(jsonSecretKey, payload);
        var signedPayload = new byte[SignatureBytes + payloadLength];
        var keyCopy = jsonSecretKey.ToArray();

        try
        {
            signature.CopyTo(signedPayload, 0);
            payload.CopyTo(signedPayload.AsSpan(SignatureBytes));

            Span<byte> zeroInitializationVector = stackalloc byte[16];
            using var aes = Aes.Create();
            aes.Key = keyCopy;

#pragma warning disable CA5358 // Guacamole JSON authentication requires AES-CBC with the protocol-defined zero IV.
            var encrypted = aes.EncryptCbc(
                signedPayload,
                zeroInitializationVector,
                PaddingMode.PKCS7);
#pragma warning restore CA5358

            return new GuacamoleLaunchToken(
                Convert.ToBase64String(encrypted),
                CreateClientIdentifier(request.ConnectionName),
                request.ConnectionName,
                request.ExpiresAtUtc);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(signature);
            CryptographicOperations.ZeroMemory(signedPayload);
            CryptographicOperations.ZeroMemory(keyCopy);
        }
    }

    private static void WritePayload(Stream destination, GuacamoleLaunchRequest request)
    {
        using var writer = new Utf8JsonWriter(destination);
        writer.WriteStartObject();
        writer.WriteString("username", request.CallerName);
        writer.WriteNumber("expires", request.ExpiresAtUtc.ToUnixTimeMilliseconds());
        writer.WritePropertyName("connections");
        writer.WriteStartObject();
        writer.WritePropertyName(request.ConnectionName);
        writer.WriteStartObject();
        writer.WriteString("id", request.SessionId);
        writer.WriteString("protocol", request.Protocol);
        writer.WritePropertyName("parameters");
        writer.WriteStartObject();
        WriteParameters(writer, request);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteParameters(Utf8JsonWriter writer, GuacamoleLaunchRequest request)
    {
        writer.WriteString("hostname", request.Host);
        writer.WriteString("port", request.Port.ToString(CultureInfo.InvariantCulture));

        if ((string.Equals(request.Protocol, RemoteTransportProtocols.Rdp, StringComparison.Ordinal)
                || string.Equals(request.Protocol, RemoteTransportProtocols.Ssh, StringComparison.Ordinal))
            && !string.IsNullOrWhiteSpace(request.UserName))
        {
            writer.WriteString("username", request.UserName);
        }

        if (!request.PasswordUtf8.IsEmpty)
        {
            writer.WriteString("password"u8, request.PasswordUtf8.Span);
        }

        if (string.Equals(request.Protocol, RemoteTransportProtocols.Rdp, StringComparison.Ordinal))
        {
            WriteDesktopParameters(writer, request);
        }
        else if (string.Equals(request.Protocol, RemoteTransportProtocols.Ssh, StringComparison.Ordinal))
        {
            writer.WriteString("font-name", "monospace");
            writer.WriteString(
                "font-size",
                NormalizeTerminalFontSize(request.TerminalFontSize).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void WriteDesktopParameters(
        Utf8JsonWriter writer,
        GuacamoleLaunchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Domain))
        {
            writer.WriteString("domain", request.Domain);
        }

        writer.WriteString("security", "any");
        writer.WriteString("ignore-cert", request.IgnoreCertificate ? "true" : "false");
        writer.WriteString(
            "server-layout",
            string.IsNullOrWhiteSpace(request.KeyboardLayout)
                ? DefaultKeyboardLayout
                : request.KeyboardLayout.Trim());
        writer.WriteString("resize-method", "reconnect");
        writer.WriteString("enable-wallpaper", "false");

        if (!string.IsNullOrWhiteSpace(request.ClientName))
        {
            writer.WriteString("client-name", request.ClientName);
        }

        if (request.EnableDrive)
        {
            writer.WriteString("enable-drive", "true");
            writer.WriteString("drive-name", request.DriveName);
            writer.WriteString("create-drive-path", "true");
            writer.WriteString("drive-path", request.DrivePath);
        }
    }

    private static string CreateClientIdentifier(string connectionName)
    {
        return Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{connectionName}\0c\0json"));
    }

    private static int NormalizeTerminalFontSize(int fontSize)
    {
        return Math.Clamp(fontSize <= 0 ? DefaultTerminalFontSize : fontSize, 8, 24);
    }

    private static void Validate(
        GuacamoleLaunchRequest request,
        ReadOnlySpan<byte> jsonSecretKey)
    {
        if (jsonSecretKey.Length != JsonSecretKeyBytes)
        {
            throw new ArgumentException(
                $"Guacamole JSON-auth keys must contain exactly {JsonSecretKeyBytes} bytes.",
                nameof(jsonSecretKey));
        }

        ValidateText(request.CallerName, 128, nameof(request.CallerName));
        ValidateText(request.ConnectionName, 256, nameof(request.ConnectionName));
        ValidateText(request.SessionId, 128, nameof(request.SessionId));
        ValidateText(request.Host, 253, nameof(request.Host));

        if (!RemoteTransportProtocols.IsSupported(request.Protocol))
        {
            throw new ArgumentException("The Remote protocol is unsupported.", nameof(request));
        }
        if (request.Port is < 1 or > 65535)
        {
            throw new ArgumentException("The target port is invalid.", nameof(request));
        }
        if (request.ExpiresAtUtc <= DateTimeOffset.UnixEpoch)
        {
            throw new ArgumentException("The launch expiry is invalid.", nameof(request));
        }
        if (!request.PasswordUtf8.IsEmpty)
        {
            try
            {
                _ = StrictUtf8.GetCharCount(request.PasswordUtf8.Span);
            }
            catch (DecoderFallbackException exception)
            {
                throw new ArgumentException(
                    "The target password must contain valid UTF-8.",
                    nameof(request),
                    exception);
            }
        }
        if (request.EnableDrive
            && (string.IsNullOrWhiteSpace(request.DriveName)
                || string.IsNullOrWhiteSpace(request.DrivePath)))
        {
            throw new ArgumentException(
                "Drive name and path are required when drive redirection is enabled.",
                nameof(request));
        }
    }

    private static void ValidateText(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("The value is invalid.", parameterName);
        }
    }
}
