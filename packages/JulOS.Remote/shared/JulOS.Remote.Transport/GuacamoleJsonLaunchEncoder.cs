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
    private const int MaximumCertificateFingerprints = 16;
    private const int MaximumPasswordBytes = 4096;
    private const int MaximumVncAutoRetry = 10;
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
            WriteDesktopParameters(writer, request, ResolveRdpOptions(request));
        }
        else if (string.Equals(request.Protocol, RemoteTransportProtocols.Vnc, StringComparison.Ordinal))
        {
            if (request.VncOptions is not null)
            {
                WriteVncParameters(writer, request.VncOptions);
            }
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
        GuacamoleLaunchRequest request,
        GuacamoleRdpOptions options)
    {
        if (!string.IsNullOrWhiteSpace(request.Domain))
        {
            writer.WriteString("domain", request.Domain);
        }

        writer.WriteString("security", options.SecurityMode);
        WriteCertificatePolicy(writer, options);
        writer.WriteString(
            "server-layout",
            string.IsNullOrWhiteSpace(request.KeyboardLayout)
                ? DefaultKeyboardLayout
                : request.KeyboardLayout.Trim());
        writer.WriteString("resize-method", options.ResizeMethod);
        WriteClipboardPolicy(writer, options.ClipboardPolicy);
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

    private static void WriteVncParameters(
        Utf8JsonWriter writer,
        GuacamoleVncOptions options)
    {
        writer.WriteString(
            "disable-display-resize",
            string.Equals(
                options.ResizePolicy,
                GuacamoleVncResizePolicies.Fixed,
                StringComparison.Ordinal)
                ? "true"
                : "false");
        WriteClipboardPolicy(writer, options.ClipboardPolicy);
        writer.WriteString("read-only", options.ReadOnly ? "true" : "false");
        writer.WriteString(
            "disable-server-input",
            options.DisableServerInput ? "true" : "false");
        writer.WriteString("swap-red-blue", options.SwapRedBlue ? "true" : "false");
        writer.WriteString("force-lossless", options.ForceLossless ? "true" : "false");

        if (string.Equals(
                options.CursorMode,
                GuacamoleVncCursorModes.Remote,
                StringComparison.Ordinal))
        {
            writer.WriteString("cursor", GuacamoleVncCursorModes.Remote);
        }
        if (!string.IsNullOrWhiteSpace(options.ClipboardEncoding))
        {
            writer.WriteString("clipboard-encoding", options.ClipboardEncoding);
        }
        if (options.ColorDepth is int colorDepth)
        {
            writer.WriteString(
                "color-depth",
                colorDepth.ToString(CultureInfo.InvariantCulture));
        }
        if (options.AutoRetry is int autoRetry)
        {
            writer.WriteString(
                "autoretry",
                autoRetry.ToString(CultureInfo.InvariantCulture));
        }
        if (options.CompressionLevel is int compressionLevel)
        {
            writer.WriteString(
                "compress-level",
                compressionLevel.ToString(CultureInfo.InvariantCulture));
        }
        if (options.QualityLevel is int qualityLevel)
        {
            writer.WriteString(
                "quality-level",
                qualityLevel.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void WriteCertificatePolicy(
        Utf8JsonWriter writer,
        GuacamoleRdpOptions options)
    {
        var ignore = string.Equals(
            options.CertificatePolicy,
            GuacamoleRdpCertificatePolicies.Ignore,
            StringComparison.Ordinal);
        writer.WriteString("ignore-cert", ignore ? "true" : "false");

        if (string.Equals(
                options.CertificatePolicy,
                GuacamoleRdpCertificatePolicies.TrustOnFirstUse,
                StringComparison.Ordinal))
        {
            writer.WriteString("cert-tofu", "true");
        }
        else if (string.Equals(
                options.CertificatePolicy,
                GuacamoleRdpCertificatePolicies.Pinned,
                StringComparison.Ordinal))
        {
            writer.WriteString(
                "cert-fingerprints",
                string.Join(
                    ",",
                    options.CertificateFingerprints.Select(NormalizeCertificateFingerprint)));
        }
    }

    private static void WriteClipboardPolicy(Utf8JsonWriter writer, string policy)
    {
        var disableCopy = policy is
            GuacamoleRdpClipboardPolicies.BrowserToRemote
            or GuacamoleRdpClipboardPolicies.Disabled;
        var disablePaste = policy is
            GuacamoleRdpClipboardPolicies.RemoteToBrowser
            or GuacamoleRdpClipboardPolicies.Disabled;

        writer.WriteString("disable-copy", disableCopy ? "true" : "false");
        writer.WriteString("disable-paste", disablePaste ? "true" : "false");
    }

    private static GuacamoleRdpOptions ResolveRdpOptions(GuacamoleLaunchRequest request)
    {
        if (!string.Equals(request.Protocol, RemoteTransportProtocols.Rdp, StringComparison.Ordinal))
        {
            if (request.RdpOptions is not null)
            {
                throw new ArgumentException(
                    "RDP options cannot be supplied for another Remote protocol.",
                    nameof(request));
            }

            throw new ArgumentException("The Remote protocol is not RDP.", nameof(request));
        }

        var options = request.RdpOptions
            ?? GuacamoleRdpOptions.CompatibilityDefaults(request.IgnoreCertificate);
        if (request.RdpOptions is not null
            && request.IgnoreCertificate
            && !string.Equals(
                options.CertificatePolicy,
                GuacamoleRdpCertificatePolicies.Ignore,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The legacy ignore-certificate flag conflicts with explicit RDP certificate policy.",
                nameof(request));
        }

        return options;
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
        ValidateOptionalText(request.UserName, 256, nameof(request.UserName));
        ValidateOptionalText(request.Domain, 256, nameof(request.Domain));
        ValidateOptionalText(request.KeyboardLayout, 64, nameof(request.KeyboardLayout));
        ValidateOptionalText(request.DriveName, 128, nameof(request.DriveName));
        ValidateOptionalText(request.DrivePath, 1024, nameof(request.DrivePath));
        ValidateOptionalText(request.ClientName, 128, nameof(request.ClientName));

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
        if (request.PasswordUtf8.Length > MaximumPasswordBytes)
        {
            throw new ArgumentException("The target password is too large.", nameof(request));
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

        if (string.Equals(request.Protocol, RemoteTransportProtocols.Rdp, StringComparison.Ordinal))
        {
            if (request.VncOptions is not null)
            {
                throw new ArgumentException(
                    "VNC options cannot be supplied for another Remote protocol.",
                    nameof(request));
            }
            ValidateRdpOptions(request, ResolveRdpOptions(request));
        }
        else if (string.Equals(request.Protocol, RemoteTransportProtocols.Vnc, StringComparison.Ordinal))
        {
            if (request.RdpOptions is not null)
            {
                throw new ArgumentException(
                    "RDP options cannot be supplied for another Remote protocol.",
                    nameof(request));
            }
            if (request.VncOptions is not null)
            {
                ValidateVncOptions(request.VncOptions, nameof(request));
            }
        }
        else
        {
            if (request.RdpOptions is not null)
            {
                throw new ArgumentException(
                    "RDP options cannot be supplied for another Remote protocol.",
                    nameof(request));
            }
            if (request.VncOptions is not null)
            {
                throw new ArgumentException(
                    "VNC options cannot be supplied for another Remote protocol.",
                    nameof(request));
            }
        }
    }

    private static void ValidateRdpOptions(
        GuacamoleLaunchRequest request,
        GuacamoleRdpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!GuacamoleRdpSecurityModes.IsSupported(options.SecurityMode))
        {
            throw new ArgumentException("The RDP security mode is unsupported.", nameof(request));
        }
        if (!GuacamoleRdpCertificatePolicies.IsSupported(options.CertificatePolicy))
        {
            throw new ArgumentException("The RDP certificate policy is unsupported.", nameof(request));
        }
        if (!GuacamoleRdpResizeMethods.IsSupported(options.ResizeMethod))
        {
            throw new ArgumentException("The RDP resize method is unsupported.", nameof(request));
        }
        if (!GuacamoleRdpClipboardPolicies.IsSupported(options.ClipboardPolicy))
        {
            throw new ArgumentException("The RDP clipboard policy is unsupported.", nameof(request));
        }
        if (GuacamoleRdpSecurityModes.RequiresPreConnectionCredentials(options.SecurityMode)
            && (string.IsNullOrWhiteSpace(request.UserName) || request.PasswordUtf8.IsEmpty))
        {
            throw new ArgumentException(
                "NLA-based RDP security requires a username and password before connection.",
                nameof(request));
        }

        if (options.CertificateFingerprints is null)
        {
            throw new ArgumentException(
                "RDP certificate fingerprints cannot be null.",
                nameof(request));
        }

        var pinned = string.Equals(
            options.CertificatePolicy,
            GuacamoleRdpCertificatePolicies.Pinned,
            StringComparison.Ordinal);
        if (pinned)
        {
            if (options.CertificateFingerprints.Count is < 1 or > MaximumCertificateFingerprints)
            {
                throw new ArgumentException(
                    $"Pinned RDP certificate policy requires from 1 through {MaximumCertificateFingerprints} fingerprints.",
                    nameof(request));
            }
            foreach (var fingerprint in options.CertificateFingerprints)
            {
                _ = NormalizeCertificateFingerprint(fingerprint);
            }
        }
        else if (options.CertificateFingerprints.Count != 0)
        {
            throw new ArgumentException(
                "RDP certificate fingerprints are allowed only with pinned certificate policy.",
                nameof(request));
        }
    }

    private static void ValidateVncOptions(
        GuacamoleVncOptions options,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!GuacamoleVncResizePolicies.IsSupported(options.ResizePolicy))
        {
            throw new ArgumentException("The VNC resize policy is unsupported.", parameterName);
        }
        if (!GuacamoleVncClipboardPolicies.IsSupported(options.ClipboardPolicy))
        {
            throw new ArgumentException("The VNC clipboard policy is unsupported.", parameterName);
        }
        if (!GuacamoleVncCursorModes.IsSupported(options.CursorMode))
        {
            throw new ArgumentException("The VNC cursor mode is unsupported.", parameterName);
        }
        if (options.ClipboardEncoding is not null
            && !GuacamoleVncClipboardEncodings.IsSupported(options.ClipboardEncoding))
        {
            throw new ArgumentException("The VNC clipboard encoding is unsupported.", parameterName);
        }
        if (options.ColorDepth is not null
            && options.ColorDepth is not (8 or 16 or 24 or 32))
        {
            throw new ArgumentException("The VNC color depth is unsupported.", parameterName);
        }
        ValidateOptionalRange(
            options.AutoRetry,
            0,
            MaximumVncAutoRetry,
            "The VNC autoretry value is invalid.",
            parameterName);
        ValidateOptionalRange(
            options.CompressionLevel,
            0,
            9,
            "The VNC compression level is invalid.",
            parameterName);
        ValidateOptionalRange(
            options.QualityLevel,
            0,
            9,
            "The VNC quality level is invalid.",
            parameterName);
    }

    private static string NormalizeCertificateFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value != value.Trim()
            || value.Length > 128
            || value.Any(char.IsControl)
            || value.Contains(','))
        {
            throw new ArgumentException("An RDP certificate fingerprint is invalid.", nameof(value));
        }

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException("An RDP certificate fingerprint is invalid.", nameof(value));
        }

        var algorithm = value[..separator].ToLowerInvariant();
        var hash = value[(separator + 1)..].Replace(":", string.Empty, StringComparison.Ordinal);
        var expectedLength = algorithm switch
        {
            "sha1" => 40,
            "sha256" => 64,
            _ => 0,
        };
        if (hash.Length != expectedLength || hash.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException("An RDP certificate fingerprint is invalid.", nameof(value));
        }

        return string.Concat(algorithm, ":", hash.ToUpperInvariant());
    }

    private static void ValidateOptionalRange(
        int? value,
        int minimum,
        int maximum,
        string message,
        string parameterName)
    {
        if (value is int actual && (actual < minimum || actual > maximum))
        {
            throw new ArgumentException(message, parameterName);
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

    private static void ValidateOptionalText(string? value, int maximumLength, string parameterName)
    {
        if (value is not null
            && (string.IsNullOrWhiteSpace(value)
                || value != value.Trim()
                || value.Length > maximumLength
                || value.Any(char.IsControl)))
        {
            throw new ArgumentException("The value is invalid.", parameterName);
        }
    }
}
