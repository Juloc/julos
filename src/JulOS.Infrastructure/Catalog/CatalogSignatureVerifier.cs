using System.Security.Cryptography;

using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>What one publisher key looks like to the verifier.</summary>
/// <param name="PublisherId">Who the key belongs to.</param>
/// <param name="KeyId">The publisher-assigned key identity.</param>
/// <param name="Algorithm">The signature algorithm the key is for.</param>
/// <param name="PublicKeySpki">Base64 SubjectPublicKeyInfo bytes.</param>
/// <param name="OfficialPinned">Whether the key is part of the built-in release configuration.</param>
/// <param name="AdministratorTrust">What an administrator decided about it.</param>
/// <param name="ValidFromUtc">Start of the key validity interval.</param>
/// <param name="ValidUntilUtc">End of the interval, or null when open-ended.</param>
/// <param name="RevokedAtUtc">When the key was revoked, or null.</param>
public sealed record CatalogVerificationKey(
    string PublisherId,
    string KeyId,
    string Algorithm,
    string PublicKeySpki,
    bool OfficialPinned,
    AdministratorTrustState AdministratorTrust,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// Verifies the detached signature envelope a catalog source publishes beside a definition.
/// </summary>
/// <remarks>
/// <para>
/// Verification answers one question — did this key sign these bytes — and trust evaluation
/// answers another. Keeping them apart is what makes an administrator decision unable to
/// rescue a signature that does not verify: the decision is only ever read after this class
/// has already said yes.
/// </para>
/// <para>
/// Key material is supplied by the caller from persisted, observed keys. Nothing here reads
/// a key out of the artifact it is checking, because a key delivered beside content proves
/// only that whoever wrote the content also wrote the key.
/// </para>
/// </remarks>
public static class CatalogSignatureVerifier
{
    /// <summary>Computes the SPKI fingerprint of a base64 public key, as <c>sha256:...</c>.</summary>
    /// <exception cref="FormatException">The key is not valid base64.</exception>
    public static string Fingerprint(string publicKeySpki)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeySpki);
        var spki = Convert.FromBase64String(publicKeySpki);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(spki))}";
    }

    /// <summary>
    /// Evaluates one definition against the keys a source published.
    /// </summary>
    /// <param name="envelope">The detached signature envelope, or null when the definition is unsigned.</param>
    /// <param name="definitionSha256">The canonical definition digest the envelope must cover.</param>
    /// <param name="keys">Keys observed from the source, already persisted with their trust decisions.</param>
    /// <param name="officialSource">Whether the definition came from the official source.</param>
    public static CatalogTrustEvaluation Evaluate(
        CatalogSignatureEnvelope? envelope,
        string definitionSha256,
        IReadOnlyCollection<CatalogVerificationKey> keys,
        bool officialSource)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (envelope is null)
        {
            return CatalogTrustEvaluator.EvaluateUnsigned();
        }

        if (envelope.SchemaVersion != CatalogSignatureInput.SchemaVersion
            || !string.Equals(envelope.Algorithm, CatalogSignatureInput.Algorithm, StringComparison.Ordinal)
            || !string.Equals(envelope.ArtifactSha256, definitionSha256, StringComparison.Ordinal))
        {
            // An envelope for other bytes, another algorithm or another schema is a claim of
            // authenticity this build cannot complete, which section 6 calls invalid rather
            // than unsigned.
            return Invalid();
        }

        var key = keys.SingleOrDefault(candidate =>
            string.Equals(candidate.PublisherId, envelope.PublisherId, StringComparison.Ordinal)
            && string.Equals(candidate.KeyId, envelope.KeyId, StringComparison.Ordinal));

        if (key is null || !string.Equals(key.Algorithm, envelope.Algorithm, StringComparison.Ordinal))
        {
            return Invalid();
        }

        string fingerprint;
        try
        {
            fingerprint = Fingerprint(key.PublicKeySpki);
        }
        catch (FormatException)
        {
            return Invalid();
        }

        var fingerprintMatches = string.Equals(fingerprint, envelope.PublicKeyFingerprint, StringComparison.Ordinal);
        var verified = fingerprintMatches && Verify(envelope, definitionSha256, key.PublicKeySpki);

        return CatalogTrustEvaluator.EvaluateSigned(
            new CatalogKeyEvidence(
                Exists: true,
                fingerprintMatches,
                verified,
                key.OfficialPinned,
                key.AdministratorTrust,
                key.ValidFromUtc,
                key.ValidUntilUtc,
                key.RevokedAtUtc),
            envelope.CreatedAtUtc,
            officialSource);
    }

    private static bool Verify(CatalogSignatureEnvelope envelope, string definitionSha256, string publicKeySpki)
    {
        byte[] signature;
        byte[] spki;
        byte[] input;
        try
        {
            signature = Convert.FromBase64String(envelope.Signature);
            spki = Convert.FromBase64String(publicKeySpki);
            input = CatalogSignatureInput.For(definitionSha256);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }

        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(spki, out _);
            return verifier.VerifyData(
                input,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            // A key that cannot be imported, or a signature of the wrong shape, is a failed
            // verification rather than a server fault.
            return false;
        }
    }

    private static CatalogTrustEvaluation Invalid() =>
        new(CatalogSignatureState.InvalidSignature, CatalogTrustPolicy.Deny, Expired: false);
}
