using System.Security.Cryptography;

namespace JulOS.Infrastructure.Packages;

/// <summary>One administrator-trusted package publisher signing key.</summary>
public sealed record TrustedPackagePublisher(string Publisher, string KeyId, string PublicKeyPem);

/// <summary>The verified immutable identity of a package artifact.</summary>
public sealed record VerifiedPackageArtifact(
    string Publisher,
    string KeyId,
    string DigestSha256,
    int ManifestLength);

/// <summary>A stable refusal raised before an untrusted package reaches installation.</summary>
public sealed class PackageArtifactVerificationException : Exception
{
    public PackageArtifactVerificationException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// Verifies the declared SHA-256 digest and an ECDSA P-256 publisher signature.
/// Trust is explicit and injected; a manifest cannot introduce its own signing key.
/// </summary>
public sealed class PackageArtifactVerifier
{
    private readonly IReadOnlyDictionary<string, TrustedPackagePublisher> trustedPublishers;

    public PackageArtifactVerifier(IEnumerable<TrustedPackagePublisher> trustedPublishers)
    {
        ArgumentNullException.ThrowIfNull(trustedPublishers);
        var indexed = new Dictionary<string, TrustedPackagePublisher>(StringComparer.Ordinal);

        foreach (var publisher in trustedPublishers)
        {
            ValidatePublisher(publisher);
            var identity = Identity(publisher.Publisher, publisher.KeyId);
            if (!indexed.TryAdd(identity, publisher))
            {
                throw new ArgumentException(
                    $"Trusted publisher key '{identity}' is configured more than once.",
                    nameof(trustedPublishers));
            }
        }

        this.trustedPublishers = indexed;
    }

    public VerifiedPackageArtifact Verify(
        ReadOnlySpan<byte> manifest,
        ReadOnlySpan<byte> signature,
        string expectedDigestSha256,
        string publisher,
        string keyId)
    {
        if (manifest.IsEmpty)
        {
            throw Failure("package.artifact.empty", "The package manifest is empty.");
        }

        if (signature.IsEmpty)
        {
            throw Failure("package.signature.missing", "The package signature is missing.");
        }

        var expectedDigest = ParseDigest(expectedDigestSha256);
        Span<byte> actualDigest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(manifest, actualDigest);

        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
        {
            throw Failure(
                "package.digest.mismatch",
                "The package artifact content does not match its declared digest.");
        }

        if (!this.trustedPublishers.TryGetValue(Identity(publisher, keyId), out var trusted))
        {
            throw Failure(
                "package.publisher.untrusted",
                "The package publisher signing key is not trusted by this installation.");
        }

        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(trusted.PublicKeyPem);
            if (!verifier.VerifyData(manifest, signature, HashAlgorithmName.SHA256))
            {
                throw Failure(
                    "package.signature.invalid",
                    "The package signature does not authenticate the manifest.");
            }
        }
        catch (PackageArtifactVerificationException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw new PackageArtifactVerificationException(
                "package.publisher.key_invalid",
                "The configured package publisher key is invalid.")
            {
                Source = exception.Source,
            };
        }

        return new VerifiedPackageArtifact(
            publisher,
            keyId,
            Convert.ToHexString(actualDigest).ToLowerInvariant(),
            manifest.Length);
    }

    private static byte[] ParseDigest(string value)
    {
        if (value.Length != SHA256.HashSizeInBytes * 2
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw Failure(
                "package.digest.invalid",
                "The declared package digest must be a 64-character SHA-256 hexadecimal value.");
        }

        return Convert.FromHexString(value);
    }

    private static void ValidatePublisher(TrustedPackagePublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        if (string.IsNullOrWhiteSpace(publisher.Publisher)
            || publisher.Publisher != publisher.Publisher.Trim()
            || string.IsNullOrWhiteSpace(publisher.KeyId)
            || publisher.KeyId != publisher.KeyId.Trim()
            || string.IsNullOrWhiteSpace(publisher.PublicKeyPem))
        {
            throw new ArgumentException("A trusted package publisher entry is invalid.");
        }
    }

    private static string Identity(string publisher, string keyId) => $"{publisher}\n{keyId}";

    private static PackageArtifactVerificationException Failure(string code, string message) =>
        new(code, message);
}
