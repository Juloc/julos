using System.Security.Cryptography;

namespace JulOS.Infrastructure.Packages;

/// <summary>One administrator-trusted package publisher signing key.</summary>
/// <param name="Publisher">Trusted publisher identity.</param>
/// <param name="KeyId">Signing-key identity.</param>
/// <param name="PublicKeyPem">ECDSA P-256 public key in PEM format.</param>
public sealed record TrustedPackagePublisher(string Publisher, string KeyId, string PublicKeyPem);

/// <summary>The verified immutable identity of a package artifact.</summary>
/// <param name="Publisher">Verified publisher identity.</param>
/// <param name="KeyId">Verified signing-key identity.</param>
/// <param name="DigestSha256">Lowercase SHA-256 artifact digest.</param>
/// <param name="ArtifactLength">Verified package archive byte length.</param>
public sealed record VerifiedPackageArtifact(
    string Publisher,
    string KeyId,
    string DigestSha256,
    int ArtifactLength);

/// <summary>A stable refusal raised before an untrusted package reaches installation.</summary>
public sealed class PackageArtifactVerificationException : Exception
{
    /// <summary>Creates an artifact verification failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    public PackageArtifactVerificationException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}

/// <summary>
/// Verifies the declared SHA-256 digest and an ECDSA P-256 publisher signature.
/// Trust is explicit and injected; package contents cannot introduce their own signing key.
/// </summary>
public sealed class PackageArtifactVerifier
{
    private readonly Dictionary<string, TrustedPackagePublisher> trustedPublishers;

    /// <summary>Creates a verifier from the installation's explicit publisher trust store.</summary>
    /// <param name="trustedPublishers">Trusted publisher keys.</param>
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

    /// <summary>Verifies digest, publisher trust and signature for one immutable package archive.</summary>
    /// <param name="artifact">Exact complete package archive bytes.</param>
    /// <param name="signature">ECDSA signature bytes over the complete archive.</param>
    /// <param name="expectedDigestSha256">Declared lowercase or uppercase SHA-256 digest.</param>
    /// <param name="publisher">Publisher identity.</param>
    /// <param name="keyId">Signing-key identity.</param>
    /// <returns>The verified immutable artifact identity.</returns>
    public VerifiedPackageArtifact Verify(
        ReadOnlySpan<byte> artifact,
        ReadOnlySpan<byte> signature,
        string expectedDigestSha256,
        string publisher,
        string keyId)
    {
        if (artifact.IsEmpty)
        {
            throw Failure("package.artifact.empty", "The package archive is empty.");
        }

        if (signature.IsEmpty)
        {
            throw Failure("package.signature.missing", "The package signature is missing.");
        }

        var expectedDigest = ParseDigest(expectedDigestSha256);
        Span<byte> actualDigest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(artifact, actualDigest);

        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
        {
            throw Failure(
                "package.digest.mismatch",
                "The package archive does not match its declared digest.");
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
            if (!verifier.VerifyData(artifact, signature, HashAlgorithmName.SHA256))
            {
                throw Failure(
                    "package.signature.invalid",
                    "The package signature does not authenticate the package archive.");
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
            artifact.Length);
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
