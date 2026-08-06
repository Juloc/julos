using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Remote.Transport;

namespace JulOS.Remote.Transport.Tests;

[TestClass]
public sealed class GuacamoleJsonLaunchEncoderTests
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 8, 4, 6, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void DesktopPayloadMatchesRequiredGuacamoleParameters()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "secret-password"u8.ToArray();
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Rdp, 3389, password) with
                {
                    Domain = "EXAMPLE",
                    IgnoreCertificate = true,
                    EnableDrive = true,
                    DriveName = "Matgate",
                    DrivePath = "/drive/11111111111141118111111111111111",
                    ClientName = "Matgate",
                },
                key);

            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            Assert.AreEqual("operator", root.GetProperty("username").GetString());
            Assert.AreEqual(Expiry.ToUnixTimeMilliseconds(), root.GetProperty("expires").GetInt64());

            var connection = root
                .GetProperty("connections")
                .GetProperty("Test desktop");
            Assert.AreEqual("session-01", connection.GetProperty("id").GetString());
            Assert.AreEqual(RemoteTransportProtocols.Rdp, connection.GetProperty("protocol").GetString());

            var parameters = connection.GetProperty("parameters");
            Assert.AreEqual("host.example.test", parameters.GetProperty("hostname").GetString());
            Assert.AreEqual("3389", parameters.GetProperty("port").GetString());
            Assert.AreEqual("remote-user", parameters.GetProperty("username").GetString());
            Assert.AreEqual("secret-password", parameters.GetProperty("password").GetString());
            Assert.AreEqual("EXAMPLE", parameters.GetProperty("domain").GetString());
            Assert.AreEqual("any", parameters.GetProperty("security").GetString());
            Assert.AreEqual("true", parameters.GetProperty("ignore-cert").GetString());
            Assert.AreEqual("de-de-qwertz", parameters.GetProperty("server-layout").GetString());
            Assert.AreEqual("reconnect", parameters.GetProperty("resize-method").GetString());
            Assert.AreEqual("false", parameters.GetProperty("enable-wallpaper").GetString());
            Assert.AreEqual("Matgate", parameters.GetProperty("client-name").GetString());
            Assert.AreEqual("true", parameters.GetProperty("enable-drive").GetString());
            Assert.AreEqual("Matgate", parameters.GetProperty("drive-name").GetString());
            Assert.AreEqual("true", parameters.GetProperty("create-drive-path").GetString());
            Assert.AreEqual(
                "/drive/11111111111141118111111111111111",
                parameters.GetProperty("drive-path").GetString());

            Assert.AreEqual(
                Convert.ToBase64String(Encoding.UTF8.GetBytes("Test desktop\0c\0json")),
                token.ClientIdentifier);
            Assert.AreEqual("Test desktop", token.ConnectionName);
            Assert.AreEqual(Expiry, token.ExpiresAtUtc);
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
    public void VncPayloadDoesNotLeakUnusedUserOrDesktopParameters()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "vnc-password"u8.ToArray();
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Vnc, 5900, password),
                key);

            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var parameters = document.RootElement
                .GetProperty("connections")
                .GetProperty("Test desktop")
                .GetProperty("parameters");

            Assert.IsFalse(parameters.TryGetProperty("username", out _));
            Assert.IsFalse(parameters.TryGetProperty("domain", out _));
            Assert.IsFalse(parameters.TryGetProperty("security", out _));
            Assert.IsFalse(parameters.TryGetProperty("font-name", out _));
            Assert.AreEqual("vnc-password", parameters.GetProperty("password").GetString());
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
    public void SecureShellPayloadNormalizesTerminalFontSize()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Ssh, 22, ReadOnlyMemory<byte>.Empty) with
                {
                    TerminalFontSize = 100,
                },
                key);

            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var parameters = document.RootElement
                .GetProperty("connections")
                .GetProperty("Test desktop")
                .GetProperty("parameters");

            Assert.AreEqual("remote-user", parameters.GetProperty("username").GetString());
            Assert.AreEqual("monospace", parameters.GetProperty("font-name").GetString());
            Assert.AreEqual("24", parameters.GetProperty("font-size").GetString());
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

    [TestMethod]
    public void SameRequestAndKeyProduceSameProtocolToken()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

        try
        {
            var request = CreateRequest(
                RemoteTransportProtocols.Rdp,
                RemoteTransportProtocols.DefaultPort(RemoteTransportProtocols.Rdp),
                ReadOnlyMemory<byte>.Empty);
            var encoder = new GuacamoleJsonLaunchEncoder();

            Assert.AreEqual(
                encoder.Encode(request, key).EncryptedData,
                encoder.Encode(request, key).EncryptedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [TestMethod]
    public void InvalidKeyProtocolAndDriveConfigurationFailClosed()
    {
        var validKey = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

        try
        {
            var encoder = new GuacamoleJsonLaunchEncoder();
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(CreateRequest(RemoteTransportProtocols.Rdp, 3389, ReadOnlyMemory<byte>.Empty), [1, 2, 3]));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(CreateRequest("website", 443, ReadOnlyMemory<byte>.Empty), validKey));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Rdp, 3389, ReadOnlyMemory<byte>.Empty) with
                    {
                        EnableDrive = true,
                    },
                    validKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(validKey);
        }
    }

    [TestMethod]
    public void ProtocolCatalogIsStableAndBounded()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                RemoteTransportProtocols.Rdp,
                RemoteTransportProtocols.Vnc,
                RemoteTransportProtocols.Ssh,
            },
            RemoteTransportProtocols.All.ToArray());
        Assert.AreEqual(3389, RemoteTransportProtocols.DefaultPort(RemoteTransportProtocols.Rdp));
        Assert.AreEqual(5900, RemoteTransportProtocols.DefaultPort(RemoteTransportProtocols.Vnc));
        Assert.AreEqual(22, RemoteTransportProtocols.DefaultPort(RemoteTransportProtocols.Ssh));
        Assert.IsFalse(RemoteTransportProtocols.IsSupported("website"));
    }

    private static GuacamoleLaunchRequest CreateRequest(
        string protocol,
        int port,
        ReadOnlyMemory<byte> password) =>
        new(
            CallerName: "operator",
            ConnectionName: "Test desktop",
            SessionId: "session-01",
            Protocol: protocol,
            Host: "host.example.test",
            Port: port,
            UserName: "remote-user",
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
