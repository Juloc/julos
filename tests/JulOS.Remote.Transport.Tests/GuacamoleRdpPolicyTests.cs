using System.Security.Cryptography;
using System.Text.Json;

using JulOS.Remote.Transport;

namespace JulOS.Remote.Transport.Tests;

[TestClass]
public sealed class GuacamoleRdpPolicyTests
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 8, 5, 8, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AllSupportedSecurityModesMapVerbatim()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "rdp-password"u8.ToArray();

        try
        {
            var modes = new[]
            {
                GuacamoleRdpSecurityModes.Any,
                GuacamoleRdpSecurityModes.NetworkLevelAuthentication,
                GuacamoleRdpSecurityModes.ExtendedNetworkLevelAuthentication,
                GuacamoleRdpSecurityModes.Tls,
                GuacamoleRdpSecurityModes.VmConnect,
                GuacamoleRdpSecurityModes.LegacyRdp,
            };

            foreach (var mode in modes)
            {
                var parameters = EncodeParameters(
                    CreateRdpRequest(password) with
                    {
                        RdpOptions = Options(securityMode: mode),
                    },
                    key);

                Assert.AreEqual(mode, parameters["security"]);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
        }
    }

    [TestMethod]
    public void CertificatePoliciesMapExactlyAndPinnedFingerprintIsNormalized()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "rdp-password"u8.ToArray();

        try
        {
            var strict = EncodeParameters(
                CreateRdpRequest(password) with
                {
                    RdpOptions = Options(
                        certificatePolicy: GuacamoleRdpCertificatePolicies.Strict),
                },
                key);
            Assert.AreEqual("false", strict["ignore-cert"]);
            Assert.IsFalse(strict.ContainsKey("cert-tofu"));
            Assert.IsFalse(strict.ContainsKey("cert-fingerprints"));

            var ignored = EncodeParameters(
                CreateRdpRequest(password) with
                {
                    RdpOptions = Options(
                        certificatePolicy: GuacamoleRdpCertificatePolicies.Ignore),
                },
                key);
            Assert.AreEqual("true", ignored["ignore-cert"]);
            Assert.IsFalse(ignored.ContainsKey("cert-tofu"));

            var tofu = EncodeParameters(
                CreateRdpRequest(password) with
                {
                    RdpOptions = Options(
                        certificatePolicy: GuacamoleRdpCertificatePolicies.TrustOnFirstUse),
                },
                key);
            Assert.AreEqual("false", tofu["ignore-cert"]);
            Assert.AreEqual("true", tofu["cert-tofu"]);

            var fingerprint = string.Concat("SHA256:", new string('a', 64));
            var pinned = EncodeParameters(
                CreateRdpRequest(password) with
                {
                    RdpOptions = Options(
                        certificatePolicy: GuacamoleRdpCertificatePolicies.Pinned,
                        fingerprints: [fingerprint]),
                },
                key);
            Assert.AreEqual("false", pinned["ignore-cert"]);
            Assert.AreEqual(
                string.Concat("sha256:", new string('A', 64)),
                pinned["cert-fingerprints"]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
        }
    }

    [TestMethod]
    public void ClipboardPoliciesMapExactDirections()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "rdp-password"u8.ToArray();

        try
        {
            var policies = new[]
            {
                (GuacamoleRdpClipboardPolicies.Bidirectional, "false", "false"),
                (GuacamoleRdpClipboardPolicies.BrowserToRemote, "true", "false"),
                (GuacamoleRdpClipboardPolicies.RemoteToBrowser, "false", "true"),
                (GuacamoleRdpClipboardPolicies.Disabled, "true", "true"),
            };

            foreach (var (policy, disableCopy, disablePaste) in policies)
            {
                var parameters = EncodeParameters(
                    CreateRdpRequest(password) with
                    {
                        RdpOptions = Options(clipboardPolicy: policy),
                    },
                    key);
                Assert.AreEqual(disableCopy, parameters["disable-copy"]);
                Assert.AreEqual(disablePaste, parameters["disable-paste"]);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
        }
    }

    [TestMethod]
    public void ResizeMethodsMapVerbatim()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "rdp-password"u8.ToArray();

        try
        {
            foreach (var method in new[]
            {
                GuacamoleRdpResizeMethods.DisplayUpdate,
                GuacamoleRdpResizeMethods.Reconnect,
            })
            {
                var parameters = EncodeParameters(
                    CreateRdpRequest(password) with
                    {
                        RdpOptions = Options(resizeMethod: method),
                    },
                    key);
                Assert.AreEqual(method, parameters["resize-method"]);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
        }
    }

    [TestMethod]
    public void NlaModesRequireCredentialsBeforeConnection()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

        try
        {
            foreach (var mode in new[]
            {
                GuacamoleRdpSecurityModes.NetworkLevelAuthentication,
                GuacamoleRdpSecurityModes.ExtendedNetworkLevelAuthentication,
            })
            {
                var request = CreateRdpRequest(ReadOnlyMemory<byte>.Empty) with
                {
                    UserName = null,
                    RdpOptions = Options(securityMode: mode),
                };

                Assert.ThrowsExactly<ArgumentException>(() =>
                    new GuacamoleJsonLaunchEncoder().Encode(request, key));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    [TestMethod]
    public void CertificatePolicyConflictsFailClosed()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
        var password = "rdp-password"u8.ToArray();

        try
        {
            var encoder = new GuacamoleJsonLaunchEncoder();
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRdpRequest(password) with
                    {
                        RdpOptions = Options(
                            certificatePolicy: GuacamoleRdpCertificatePolicies.Pinned),
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRdpRequest(password) with
                    {
                        RdpOptions = Options(
                            certificatePolicy: GuacamoleRdpCertificatePolicies.Strict,
                            fingerprints: [string.Concat("sha256:", new string('A', 64))]),
                    },
                    key));
            Assert.ThrowsExactly<ArgumentException>(() =>
                encoder.Encode(
                    CreateRdpRequest(password) with
                    {
                        IgnoreCertificate = true,
                        RdpOptions = Options(
                            certificatePolicy: GuacamoleRdpCertificatePolicies.Strict),
                    },
                    key));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(password);
        }
    }

    [TestMethod]
    public void RdpOptionsAreRejectedForVncAndSsh()
    {
        var key = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");

        try
        {
            var encoder = new GuacamoleJsonLaunchEncoder();
            foreach (var protocol in new[]
            {
                RemoteTransportProtocols.Vnc,
                RemoteTransportProtocols.Ssh,
            })
            {
                var request = CreateRdpRequest(ReadOnlyMemory<byte>.Empty) with
                {
                    Protocol = protocol,
                    Port = RemoteTransportProtocols.DefaultPort(protocol),
                    RdpOptions = Options(),
                };

                Assert.ThrowsExactly<ArgumentException>(() => encoder.Encode(request, key));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static GuacamoleRdpOptions Options(
        string securityMode = GuacamoleRdpSecurityModes.Any,
        string certificatePolicy = GuacamoleRdpCertificatePolicies.Strict,
        IReadOnlyList<string>? fingerprints = null,
        string resizeMethod = GuacamoleRdpResizeMethods.DisplayUpdate,
        string clipboardPolicy = GuacamoleRdpClipboardPolicies.Bidirectional) =>
        new(
            securityMode,
            certificatePolicy,
            fingerprints ?? Array.Empty<string>(),
            resizeMethod,
            clipboardPolicy);

    private static GuacamoleLaunchRequest CreateRdpRequest(ReadOnlyMemory<byte> password) =>
        new(
            CallerName: "operator",
            ConnectionName: "RDP policy test",
            SessionId: "session-rdp-policy",
            Protocol: RemoteTransportProtocols.Rdp,
            Host: "rdp.example.test",
            Port: 3389,
            UserName: "remote-user",
            PasswordUtf8: password,
            Domain: "EXAMPLE",
            IgnoreCertificate: false,
            KeyboardLayout: "de-de-qwertz",
            TerminalFontSize: 12,
            EnableDrive: false,
            DriveName: null,
            DrivePath: null,
            ClientName: "JulOS",
            ExpiresAtUtc: Expiry);

    private static Dictionary<string, string> EncodeParameters(
        GuacamoleLaunchRequest request,
        ReadOnlySpan<byte> key)
    {
        var token = new GuacamoleJsonLaunchEncoder().Encode(request, key);
        var payload = DecryptAndVerify(token.EncryptedData, key);
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement
                .GetProperty("connections")
                .GetProperty(request.ConnectionName)
                .GetProperty("parameters")
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.GetString()
                        ?? throw new AssertFailedException("RDP parameter was not a string."),
                    StringComparer.Ordinal);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

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
