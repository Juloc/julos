using System.Security.Cryptography;

using JulOS.Domain.Packages;

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

/// <summary>What verification concluded about one package artifact.</summary>
/// <param name="DigestSha256">Lowercase SHA-256 artifact digest, always verified.</param>
/// <param name="ArtifactLength">Verified package archive byte length.</param>
/// <param name="SignatureState">How much is known about who produced it.</param>
/// <param name="Publisher">Who claimed to produce it, or null when it carries no signature.</param>
/// <param name="KeyId">Which key signed it, or null when it carries no signature.</param>
/// <param name="PublicKeyFingerprint">The SPKI fingerprint of that key, or null when unsigned.</param>
/// <remarks>
/// An evaluation is only ever produced for an artifact whose digest matched. Integrity is not
/// one of the outcomes: a claim about who produced bytes is meaningless until it is settled
/// which bytes those are.
/// </remarks>
public sealed record PackageArtifactEvaluation(
    string DigestSha256,
    int ArtifactLength,
    PackageSignatureState SignatureState,
    string? Publisher,
    string? KeyId,
    string? PublicKeyFingerprint);

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
    /// <param name="signature">ECDSA P-256/SHA-256 signature bytes over the complete archive in IEEE P1363 format.</param>
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
        if (signature.IsEmpty)
        {
            throw Failure("package.signature.missing", "The package signature is missing.");
        }

        // This overload is the path that requires trust rather than merely reporting it, so
        // an unknown publisher is refused here with its own stable code before evaluation
        // would report it as a state.
        if (!this.trustedPublishers.ContainsKey(Identity(publisher, keyId)))
        {
            throw Failure(
                "package.publisher.untrusted",
                "The package publisher signing key is not trusted by this installation.");
        }

        var evaluation = this.Evaluate(artifact, signature, expectedDigestSha256, publisher, keyId, null);
        return new VerifiedPackageArtifact(
            publisher,
            keyId,
            evaluation.DigestSha256,
            evaluation.ArtifactLength);
    }

    /// <summary>
    /// Verifies the digest and reports how much is known about who produced the artifact.
    /// </summary>
    /// <param name="artifact">Exact complete package archive bytes.</param>
    /// <param name="signature">Signature over the complete archive, or empty when unsigned.</param>
    /// <param name="expectedDigestSha256">Declared SHA-256 digest.</param>
    /// <param name="publisher">Claimed publisher identity, or null when unsigned.</param>
    /// <param name="keyId">Claimed signing-key identity, or null when unsigned.</param>
    /// <param name="publicKeySpkiBase64">
    /// Base64 SubjectPublicKeyInfo supplied with the upload, used only when this installation
    /// has no configured key for the claimed identity.
    /// </param>
    /// <remarks>
    /// <para>
    /// The digest is settled first and a mismatch always fails, because a claim about who
    /// produced bytes means nothing until it is settled which bytes those are. Only then does
    /// this report trust, and reporting is all it does: an unsigned or unknown-signed artifact
    /// is a state the caller must decide about, not a refusal.
    /// </para>
    /// <para>
    /// A supplied key can produce <see cref="PackageSignatureState.UnknownSigned"/> and never
    /// <see cref="PackageSignatureState.TrustedSigned"/> — trust comes from configuration, so
    /// an artifact describing its own key proves only that whoever wrote the artifact also
    /// wrote the key. An artifact claiming an identity this installation does have a key for,
    /// but supplying different bytes for it, is refused rather than downgraded to unknown.
    /// </para>
    /// </remarks>
    /// <exception cref="PackageArtifactVerificationException">
    /// The archive is empty, the digest does not match, or a claimed signature cannot be
    /// completed.
    /// </exception>
    public PackageArtifactEvaluation Evaluate(
        ReadOnlySpan<byte> artifact,
        ReadOnlySpan<byte> signature,
        string expectedDigestSha256,
        string? publisher,
        string? keyId,
        string? publicKeySpkiBase64)
    {
        if (artifact.IsEmpty)
        {
            throw Failure("package.artifact.empty", "The package archive is empty.");
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

        var digest = Convert.ToHexStringLower(actualDigest);
        var claimsSignature = !signature.IsEmpty
            || !string.IsNullOrWhiteSpace(publisher)
            || !string.IsNullOrWhiteSpace(keyId)
            || !string.IsNullOrWhiteSpace(publicKeySpkiBase64);

        if (!claimsSignature)
        {
            return new PackageArtifactEvaluation(
                digest,
                artifact.Length,
                PackageSignatureState.NotSigned,
                Publisher: null,
                KeyId: null,
                PublicKeyFingerprint: null);
        }

        if (signature.IsEmpty || string.IsNullOrWhiteSpace(publisher) || string.IsNullOrWhiteSpace(keyId))
        {
            // Half a claim is still a claim of authenticity that cannot be completed, which
            // section 6 calls invalid rather than unsigned.
            throw Failure(
                "package.signature.invalid",
                "The package claims a publisher signature without everything needed to check it.");
        }

        var trusted = this.trustedPublishers.GetValueOrDefault(Identity(publisher, keyId));
        var (publicKey, isTrustedKey) = ResolveKey(trusted, publicKeySpkiBase64);

        string fingerprint;
        using (publicKey)
        {
            fingerprint = Fingerprint(publicKey);
            if (!VerifySignature(publicKey, artifact, signature))
            {
                throw Failure(
                    "package.signature.invalid",
                    "The package signature does not authenticate the package archive.");
            }
        }

        return new PackageArtifactEvaluation(
            digest,
            artifact.Length,
            isTrustedKey ? PackageSignatureState.TrustedSigned : PackageSignatureState.UnknownSigned,
            publisher,
            keyId,
            fingerprint);
    }

    /// <summary>Computes the SPKI fingerprint of a key, as <c>sha256:...</c>.</summary>
    /// <param name="publicKey">The key to fingerprint.</param>
    public static string Fingerprint(ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo()))}";
    }

    private static (ECDsa Key, bool Trusted) ResolveKey(
        TrustedPackagePublisher? trusted,
        string? publicKeySpkiBase64)
    {
        if (trusted is not null)
        {
            var configured = ImportPem(trusted.PublicKeyPem);
            if (publicKeySpkiBase64 is { Length: > 0 }
                && !string.Equals(
                    Fingerprint(configured),
                    Fingerprint(ImportSpki(publicKeySpkiBase64)),
                    StringComparison.Ordinal))
            {
                configured.Dispose();

                // Claiming a trusted identity while supplying other key bytes is a
                // substitution attempt, not an unknown publisher.
                throw Failure(
                    "package.publisher.key_conflict",
                    "The package claims a trusted publisher key but supplies different key material.");
            }

            return (configured, true);
        }

        return publicKeySpkiBase64 is { Length: > 0 }
            ? (ImportSpki(publicKeySpkiBase64), false)
            : throw Failure(
                "package.signature.key_unavailable",
                "No key is available to check the signature this package claims.");
    }

    private static ECDsa ImportPem(string pem)
    {
        try
        {
            var key = ECDsa.Create();
            key.ImportFromPem(pem);
            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            throw new PackageArtifactVerificationException(
                "package.publisher.key_invalid",
                "The configured package publisher key is invalid.")
            {
                Source = exception.Source,
            };
        }
    }

    private static ECDsa ImportSpki(string base64)
    {
        try
        {
            var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64), out _);
            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new PackageArtifactVerificationException(
                "package.signature.invalid",
                "The supplied package publisher key could not be read.")
            {
                Source = exception.Source,
            };
        }
    }

    private static bool VerifySignature(ECDsa publicKey, ReadOnlySpan<byte> artifact, ReadOnlySpan<byte> signature)
    {
        try
        {
            return publicKey.VerifyData(
                artifact,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            // A signature of the wrong shape is a failed verification, not a server fault.
            return false;
        }
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
