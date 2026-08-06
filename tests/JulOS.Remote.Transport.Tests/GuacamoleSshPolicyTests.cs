using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Remote.Transport;

namespace JulOS.Remote.Transport.Tests;

[TestClass]
public sealed class GuacamoleSshPolicyTests
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void PublicKeyPolicyMapsExactGuacamoleParameters()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var privateKey = Encoding.UTF8.GetBytes(
            "-----BEGIN OPENSSH PRIVATE KEY-----\nYWJj\n-----END OPENSSH PRIVATE KEY-----\n");
        var passphrase = "key-secret"u8.ToArray();
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Ssh, ReadOnlyMemory<byte>.Empty) with
                {
                    SshOptions = CreateOptions() with
                    {
                        AuthenticationMode = GuacamoleSshAuthenticationModes.PublicKey,
                        HostKeyPolicy = GuacamoleSshHostKeyPolicies.Strict,
                        HostKey = "host.example.test ssh-ed25519 YWJj",
                        PrivateKeyUtf8 = privateKey,
                        PassphraseUtf8 = passphrase,
                        TerminalFontName = "Cascadia Mono",
                        TerminalFontSize = 16,
                        TimeoutSeconds = 20,
                        ServerAliveIntervalSeconds = 30,
                    },
                },
                key);

            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var parameters = GetParameters(document);

            Assert.AreEqual("remote-user", parameters.GetProperty("username").GetString());
            Assert.IsFalse(parameters.TryGetProperty("password", out _));
            Assert.AreEqual(
                Encoding.UTF8.GetString(privateKey),
                parameters.GetProperty("private-key").GetString());
            Assert.AreEqual("key-secret", parameters.GetProperty("passphrase").GetString());
            Assert.AreEqual(
                "host.example.test ssh-ed25519 YWJj",
                parameters.GetProperty("host-key").GetString());
            Assert.AreEqual("Cascadia Mono", parameters.GetProperty("font-name").GetString());
            Assert.AreEqual("16", parameters.GetProperty("font-size").GetString());
            Assert.AreEqual("20", parameters.GetProperty("timeout").GetString());
            Assert.AreEqual("30", parameters.GetProperty("server-alive-interval").GetString());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(privateKey);
            CryptographicOperations.ZeroMemory(passphrase);
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    [TestMethod]
    [DataRow(GuacamoleSshAuthenticationModes.Password, true)]
    [DataRow(GuacamoleSshAuthenticationModes.None, false)]
    public void PasswordAndNonePoliciesExcludePrivateKeyMaterial(
        string authenticationMode,
        bool includePassword)
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = includePassword ? "ssh-password"u8.ToArray() : [];
        byte[]? payload = null;

        try
        {
            var token = new GuacamoleJsonLaunchEncoder().Encode(
                CreateRequest(RemoteTransportProtocols.Ssh, password) with
                {
                    SshOptions = CreateOptions() with
                    {
                        AuthenticationMode = authenticationMode,
                    },
                },
                key);

            payload = DecryptAndVerify(token.EncryptedData, key);
            using var document = JsonDocument.Parse(payload);
            var parameters = GetParameters(document);

            Assert.AreEqual(includePassword, parameters.TryGetProperty("password", out _));
            Assert.IsFalse(parameters.TryGetProperty("private-key", out _));
            Assert.IsFalse(parameters.TryGetProperty("passphrase", out _));
            Assert.IsFalse(parameters.TryGetProperty("host-key", out _));
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
    public void InvalidAndCrossProtocolPoliciesFailClosed()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "ssh-password"u8.ToArray();
        var invalidPrivateKey = "not-an-openssh-key"u8.ToArray();

        try
        {
            var encoder = new GuacamoleJsonLaunchEncoder();
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Ssh, password) with
                    {
                        SshOptions = CreateOptions() with
                        {
                            HostKeyPolicy = GuacamoleSshHostKeyPolicies.Strict,
                        },
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Ssh, ReadOnlyMemory<byte>.Empty) with
                    {
                        SshOptions = CreateOptions() with
                        {
                            AuthenticationMode = GuacamoleSshAuthenticationModes.PublicKey,
                            PrivateKeyUtf8 = invalidPrivateKey,
                        },
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Ssh, password) with
                    {
                        SshOptions = CreateOptions() with
                        {
                            ServerAliveIntervalSeconds = 1,
                        },
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Rdp, ReadOnlyMemory<byte>.Empty) with
                    {
                        SshOptions = CreateOptions(),
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRequest(RemoteTransportProtocols.Vnc, ReadOnlyMemory<byte>.Empty) with
                    {
                        SshOptions = CreateOptions(),
                    },
                    key));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
            CryptographicOperations.ZeroMemory(invalidPrivateKey);
        }
    }

    private static JsonElement GetParameters(JsonDocument document) =>
        document.RootElement
            .GetProperty("connections")
            .GetProperty("Test terminal")
            .GetProperty("parameters");

    private static GuacamoleSshOptions CreateOptions() =>
        new(
            GuacamoleSshAuthenticationModes.Password,
            GuacamoleSshHostKeyPolicies.Disabled,
            HostKey: null,
            PrivateKeyUtf8: ReadOnlyMemory<byte>.Empty,
            PassphraseUtf8: ReadOnlyMemory<byte>.Empty,
            TerminalFontName: "monospace",
            TerminalFontSize: 12,
            TimeoutSeconds: 10,
            ServerAliveIntervalSeconds: 0);

    private static GuacamoleLaunchRequest CreateRequest(
        string protocol,
        ReadOnlyMemory<byte> password) =>
        new(
            CallerName: "operator",
            ConnectionName: "Test terminal",
            SessionId: "session-02",
            Protocol: protocol,
            Host: "host.example.test",
            Port: RemoteTransportProtocols.DefaultPort(protocol),
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
