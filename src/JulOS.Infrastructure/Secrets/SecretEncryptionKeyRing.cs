using System.Security.Cryptography;

namespace JulOS.Infrastructure.Secrets;

/// <summary>Loads AES-256 keys from an external directory that is not part of database backups.</summary>
internal sealed class SecretEncryptionKeyRing : IDisposable
{
    private const int KeySizeBytes = 32;
    private readonly Dictionary<string, byte[]> keys;
    private bool disposed;

    private SecretEncryptionKeyRing(string activeKeyId, Dictionary<string, byte[]> keys)
    {
        this.ActiveKeyId = activeKeyId;
        this.keys = keys;
    }

    internal string ActiveKeyId { get; }

    internal static SecretEncryptionKeyRing Load(string activeKeyId, string keyRingPath)
    {
        ValidateKeyId(activeKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRingPath);

        if (!Path.IsPathFullyQualified(keyRingPath))
        {
            throw new InvalidOperationException("Secrets:KeyRingPath must be an absolute path.");
        }

        if (!Directory.Exists(keyRingPath))
        {
            throw new InvalidOperationException("The configured secret key-ring directory does not exist.");
        }

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (var path in Directory.EnumerateFiles(keyRingPath, "*.key", SearchOption.TopDirectoryOnly))
            {
                var keyId = Path.GetFileNameWithoutExtension(path);
                ValidateKeyId(keyId);

                byte[] value;
                try
                {
                    value = Convert.FromBase64String(File.ReadAllText(path).Trim());
                }
                catch (FormatException exception)
                {
                    throw new InvalidOperationException($"Secret key file '{keyId}.key' is not valid Base64.", exception);
                }

                if (value.Length != KeySizeBytes)
                {
                    CryptographicOperations.ZeroMemory(value);
                    throw new InvalidOperationException($"Secret key file '{keyId}.key' must contain exactly 32 decoded bytes.");
                }

                if (!keys.TryAdd(keyId, value))
                {
                    CryptographicOperations.ZeroMemory(value);
                    throw new InvalidOperationException($"The secret key identifier '{keyId}' is duplicated.");
                }
            }

            if (!keys.ContainsKey(activeKeyId))
            {
                throw new InvalidOperationException("The active secret-encryption key is not present in the configured key ring.");
            }

            return new SecretEncryptionKeyRing(activeKeyId, keys);
        }
        catch
        {
            foreach (var key in keys.Values)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            throw;
        }
    }

    internal byte[] GetKey(string keyId)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        return this.keys.TryGetValue(keyId, out var key)
            ? key
            : throw new KeyNotFoundException("The requested secret-encryption key is not available.");
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        foreach (var key in this.keys.Values)
        {
            CryptographicOperations.ZeroMemory(key);
        }

        this.keys.Clear();
        this.disposed = true;
        GC.SuppressFinalize(this);
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)
            || keyId.Length > 64
            || !string.Equals(keyId, keyId.Trim(), StringComparison.Ordinal)
            || keyId.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-' or '_')))
        {
            throw new InvalidOperationException("A secret key identifier must contain only letters, digits, '.', '-' or '_'.");
        }
    }
}
