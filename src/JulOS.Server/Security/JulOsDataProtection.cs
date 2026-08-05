using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using JulOS.Server.Secrets;

using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

namespace JulOS.Server.Security;

internal static class JulOsDataProtection
{
    internal const string KeyRingPath = "/var/lib/julos/data-protection";
}

/// <summary>Reads the active JulOS encryption key for Data Protection.</summary>
public sealed class JulOsDataProtectionKeyProvider
{
    private const int KeySize = 32;
    private readonly SecretReferenceOptions options;

    /// <summary>Creates the provider from the validated JulOS secret configuration.</summary>
    public JulOsDataProtectionKeyProvider(IConfiguration configuration)
    {
        options = SecretReferenceOptions.Read(configuration);
    }

    internal string ActiveKeyId => options.ActiveKeyId;

    internal byte[] ReadKey(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new InvalidOperationException("A data-protection key identifier is required.");
        }

        var keyPath = Path.Combine(options.KeyRingPath, $"{keyId}.key");
        var encodedKey = File.ReadAllText(keyPath).Trim();
        var key = new byte[KeySize];

        var decoded = encodedKey.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)
            ? Convert.TryFromHexString(encodedKey.AsSpan(4), key, out var bytesWritten)
                && bytesWritten == KeySize
            : Convert.TryFromBase64String(encodedKey, key, out bytesWritten)
                && bytesWritten == KeySize;

        if (!decoded)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                $"The encryption key '{keyId}' must contain exactly {KeySize} bytes.");
        }

        return key;
    }
}

/// <summary>Connects the JulOS XML encryptor to ASP.NET Core Data Protection.</summary>
public sealed class JulOsDataProtectionOptions : IConfigureOptions<KeyManagementOptions>
{
    private readonly JulOsDataProtectionKeyProvider keyProvider;

    /// <summary>Creates the Data Protection options adapter.</summary>
    public JulOsDataProtectionOptions(JulOsDataProtectionKeyProvider keyProvider)
    {
        this.keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.XmlEncryptor = new JulOsDataProtectionXmlEncryptor(keyProvider);
    }
}

internal sealed class JulOsDataProtectionXmlEncryptor(
    JulOsDataProtectionKeyProvider keyProvider) : IXmlEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AdditionalData = "JulOS.DataProtection.v1"u8.ToArray();

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var keyId = keyProvider.ActiveKeyId;
        var key = keyProvider.ReadKey(keyId);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(
            plaintextElement.ToString(SaveOptions.DisableFormatting));
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AdditionalData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var encryptedElement = new XElement(
            "julosDataProtection",
            new XAttribute("version", 1),
            new XAttribute("keyId", keyId),
            new XAttribute("nonce", Convert.ToBase64String(nonce)),
            new XAttribute("tag", Convert.ToBase64String(tag)),
            Convert.ToBase64String(ciphertext));

        return new EncryptedXmlInfo(
            encryptedElement,
            typeof(JulOsDataProtectionXmlDecryptor));
    }
}

/// <summary>Decrypts ASP.NET Core Data Protection keys with the JulOS primary key.</summary>
public sealed class JulOsDataProtectionXmlDecryptor : IXmlDecryptor
{
    private const int TagSize = 16;
    private static readonly byte[] AdditionalData = "JulOS.DataProtection.v1"u8.ToArray();
    private readonly JulOsDataProtectionKeyProvider keyProvider;

    /// <summary>Creates the decryptor through the ASP.NET Core service activator.</summary>
    public JulOsDataProtectionXmlDecryptor(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        keyProvider = services.GetRequiredService<JulOsDataProtectionKeyProvider>();
    }

    /// <inheritdoc />
    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        if ((int?)encryptedElement.Attribute("version") != 1)
        {
            throw new InvalidOperationException("The data-protection key format is not supported.");
        }

        var keyId = ReadRequiredAttribute(encryptedElement, "keyId");
        var nonce = Convert.FromBase64String(ReadRequiredAttribute(encryptedElement, "nonce"));
        var tag = Convert.FromBase64String(ReadRequiredAttribute(encryptedElement, "tag"));
        var ciphertext = Convert.FromBase64String(encryptedElement.Value);
        var plaintext = new byte[ciphertext.Length];
        var key = keyProvider.ReadKey(keyId);

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AdditionalData);
            return XElement.Parse(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string ReadRequiredAttribute(XElement element, XName name)
    {
        var value = (string?)element.Attribute(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"The encrypted data-protection key is missing '{name}'.")
            : value;
    }
}
