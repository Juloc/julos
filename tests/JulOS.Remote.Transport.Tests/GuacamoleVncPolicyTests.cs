using System.Security.Cryptography;
using System.Text.Json;

using JulOS.Remote.Transport;

namespace JulOS.Remote.Transport.Tests;

[TestClass]
public sealed class GuacamoleVncPolicyTests
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ExplicitPolicyMapsExactGuacamoleParameters()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "vnc-password"u8.ToArray();
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Vnc, password) with
                {
                    VncOptions = CreateOptions() with
                    {
                        ResizePolicy = GuacamoleVncResizePolicies.Fixed,
                        ClipboardPolicy = GuacamoleVncClipboardPolicies.RemoteToBrowser,
                        CursorMode = GuacamoleVncCursorModes.Remote,
                        ClipboardEncoding = GuacamoleVncClipboardEncodings.Utf8,
                        ColorDepth = 24,
                        AutoRetry = 3,
                        CompressionLevel = 7,
                        QualityLevel = 8,
                        ReadOnly = true,
                        DisableServerInput = true,
                        SwapRedBlue = true,
                        ForceLossless = true,
                    },
                },
                key);

            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var parameters = GetParameters(document);

            Assert.AreEqual("vnc-password", parameters.GetProperty("password").GetString());
            Assert.IsFalse(parameters.TryGetProperty("username", out _));
            Assert.AreEqual("true", parameters.GetProperty("disable-display-resize").GetString());
            Assert.AreEqual("false", parameters.GetProperty("disable-copy").GetString());
            Assert.AreEqual("true", parameters.GetProperty("disable-paste").GetString());
            Assert.AreEqual("remote", parameters.GetProperty("cursor").GetString());
            Assert.AreEqual("UTF-8", parameters.GetProperty("clipboard-encoding").GetString());
            Assert.AreEqual("24", parameters.GetProperty("color-depth").GetString());
            Assert.AreEqual("3", parameters.GetProperty("autoretry").GetString());
            Assert.AreEqual("7", parameters.GetProperty("compress-level").GetString());
            Assert.AreEqual("8", parameters.GetProperty("quality-level").GetString());
            Assert.AreEqual("true", parameters.GetProperty("read-only").GetString());
            Assert.AreEqual("true", parameters.GetProperty("disable-server-input").GetString());
            Assert.AreEqual("true", parameters.GetProperty("swap-red-blue").GetString());
            Assert.AreEqual("true", parameters.GetProperty("force-lossless").GetString());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    [TestMethod]
    [DataRow(GuacamoleVncResizePolicies.Dynamic, "false")]
    [DataRow(GuacamoleVncResizePolicies.Fixed, "true")]
    public void ResizePolicyMapsExactDisplayResizeValue(string policy, string expected)
    {
        using var document = EncodePolicy(CreateOptions() with { ResizePolicy = policy });
        Assert.AreEqual(
            expected,
            GetParameters(document).GetProperty("disable-display-resize").GetString());
    }

    [TestMethod]
    [DataRow(GuacamoleVncClipboardPolicies.Bidirectional, "false", "false")]
    [DataRow(GuacamoleVncClipboardPolicies.BrowserToRemote, "true", "false")]
    [DataRow(GuacamoleVncClipboardPolicies.RemoteToBrowser, "false", "true")]
    [DataRow(GuacamoleVncClipboardPolicies.Disabled, "true", "true")]
    public void ClipboardPolicyMapsExactRestrictions(
        string policy,
        string expectedDisableCopy,
        string expectedDisablePaste)
    {
        using var document = EncodePolicy(CreateOptions() with { ClipboardPolicy = policy });
        var parameters = GetParameters(document);

        Assert.AreEqual(expectedDisableCopy, parameters.GetProperty("disable-copy").GetString());
        Assert.AreEqual(expectedDisablePaste, parameters.GetProperty("disable-paste").GetString());
    }

    [TestMethod]
    public void InvalidPolicyAndCrossProtocolOptionsFailClosed()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

        try
        {
            var encoder = new GuacamoleJsonLaunchEncoder();
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Vnc, ReadOnlyMemory<byte>.Empty) with
                    {
                        VncOptions = CreateOptions() with { ColorDepth = 12 },
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Vnc, ReadOnlyMemory<byte>.Empty) with
                    {
                        VncOptions = CreateOptions() with { AutoRetry = 11 },
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Vnc, ReadOnlyMemory<byte>.Empty) with
                    {
                        VncOptions = CreateOptions() with { ClipboardEncoding = "UTF-32" },
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Rdp, ReadOnlyMemory<byte>.Empty) with
                    {
                        VncOptions = CreateOptions(),
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Ssh, ReadOnlyMemory<byte>.Empty) with
                    {
                        VncOptions = CreateOptions(),
                    },
                    key));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [TestMethod]
    public void OmittedPolicyPreservesLegacyVncPayload()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Vnc, ReadOnlyMemory<byte>.Empty),
                key);
            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var parameters = GetParameters(document);

            Assert.IsFalse(parameters.TryGetProperty("disable-display-resize", out _));
            Assert.IsFalse(parameters.TryGetProperty("disable-copy", out _));
            Assert.IsFalse(parameters.TryGetProperty("disable-paste", out _));
            Assert.IsFalse(parameters.TryGetProperty("cursor", out _));
            Assert.IsFalse(parameters.TryGetProperty("autoretry", out _));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private static JsonDocument EncodePolicy(GuacamoleVncOptions options)
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Vnc, ReadOnlyMemory<byte>.Empty) with
                {
                    VncOptions = options,
                },
                key);
            payload = DecryptAndVerify(token.EncryptedData, key);
            return JsonDocument.Parse(payload.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    private static JsonElement GetParameters(JsonDocument document) =>
        document.RootElement
            .GetProperty("connections")
            .GetProperty("Test desktop")
            .GetProperty("parameters");

    private static GuacamoleVncOptions CreateOptions() =>
        new(
            GuacamoleVncResizePolicies.Dynamic,
            GuacamoleVncClipboardPolicies.Bidirectional,
            GuacamoleVncCursorModes.Local,
            GuacamoleVncClipboardEncodings.Iso88591,
            ColorDepth: null,
            AutoRetry: null,
            CompressionLevel: null,
            QualityLevel: null,
            ReadOnly: false,
            DisableServerInput: false,
            SwapRedBlue: false,
            ForceLossless: false);

    private static GuacamoleLaunchRequest CreateRequest(
        string protocol,
        ReadOnlyMemory<byte> password) =>
        new(
            CallerName: "operator",
            ConnectionName: "Test desktop",
            SessionId: "session-01",
            Protocol: protocol,
            Host: "host.example.test",
            Port: RemoteTransportProtocols.DefaultPort(protocol),
            UserName: "unused-vnc-user",
            PasswordUtf8: password,
            Domain: null,
            IgnoreCertificate: false,
            KeyboardLayout: null,
            TerminalFontSize: 12,
            EnableDrive: false,
            DriveName: null,
            DrivePath: null,
            ClientName: null,
            ExpiresAtUtc: Expiry);

    private static byte[] DecryptAndVerify(string encryptedData, ReadOnlySpan<byte> key)
    {
        var encrypted = Convert.FromBase64String(encryptedData);
        Span<byte> zeroInitializationVector = stackalloc byte[16];
        byte[] decrypted;

        using (var aes = Aes.Create())
        {
            aes.Key = key.ToArray();
#pragma warning disable CA5358 // The test verifies the Guacamole-required AES-CBC JSON-auth format.
            decrypted = aes.DecryptCbc(
                encrypted,
                zeroInitializationVector,
                PaddingMode.PKCS7);
#pragma warning restore CA5358
            CryptographicOperations.ZeroMemory(aes.Key);
        }

        CryptographicOperations.ZeroMemory(encrypted);
        var signature = decrypted.AsSpan(0, 32);
        var payload = decrypted.AsSpan(32);
        var expectedSignature = HMACSHA256.HashData(key, payload);

        try
        {
            Assert.IsTrue(CryptographicOperations.FixedTimeEquals(signature, expectedSignature));
            return payload.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedSignature);
            CryptographicOperations.ZeroMemory(decrypted);
        }
    }
}
