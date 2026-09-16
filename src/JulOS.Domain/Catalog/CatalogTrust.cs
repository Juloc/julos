namespace JulOS.Domain.Catalog;

/// <summary>
/// What verification concluded about one catalog definition's signature.
/// </summary>
/// <remarks>
/// Source trust and artifact signature are independent indicators, as
/// <c>docs/APPLICATION_CATALOG.md</c> section 6 states. This describes only the second:
/// whether the bytes are authentic and whether the key that vouches for them is one this
/// installation trusts.
/// </remarks>
public enum CatalogSignatureState
{
    /// <summary>Verified with a key that is official-pinned or administrator-trusted.</summary>
    TrustedSigned = 1,

    /// <summary>Cryptographically valid, but the key is not one this installation trusts.</summary>
    UnknownSigned = 2,

    /// <summary>No signature envelope was published for the definition.</summary>
    /// <remarks>Named NotSigned rather than Unsigned so it is not read as a numeric type.</remarks>
    NotSigned = 3,

    /// <summary>
    /// The definition claims authenticity and fails, or verification cannot be completed.
    /// </summary>
    /// <remarks>
    /// A missing key is this, not <see cref="NotSigned"/>. An artifact that says it is signed
    /// and cannot prove it is a stronger claim than one that never said so.
    /// </remarks>
    InvalidSignature = 4,
}

/// <summary>What an administrator has decided about one publisher key.</summary>
public enum AdministratorTrustState
{
    /// <summary>Seen, never decided.</summary>
    Unknown = 1,

    /// <summary>Explicitly trusted by an administrator.</summary>
    Trusted = 2,

    /// <summary>Explicitly distrusted by an administrator.</summary>
    Distrusted = 3,
}

/// <summary>Why installation of an otherwise valid definition is still denied.</summary>
public enum CatalogTrustPolicy
{
    /// <summary>Installation may proceed, possibly after a warning.</summary>
    Allow = 1,

    /// <summary>Installation is denied whatever the signature state says.</summary>
    Deny = 2,
}

/// <summary>What is known about the key a definition was signed with.</summary>
/// <param name="Exists">Whether a usable key was found at all.</param>
/// <param name="FingerprintMatches">Whether the calculated SPKI fingerprint matches key set and envelope.</param>
/// <param name="SignatureVerifies">Whether the signature verifies over the canonical input.</param>
/// <param name="OfficialPinned">Whether the key is part of the built-in release configuration.</param>
/// <param name="AdministratorTrust">What an administrator decided about it.</param>
/// <param name="ValidFromUtc">Start of the key validity interval.</param>
/// <param name="ValidUntilUtc">End of the key validity interval, or null when open-ended.</param>
/// <param name="RevokedAtUtc">When the key was revoked, or null.</param>
public readonly record struct CatalogKeyEvidence(
    bool Exists,
    bool FingerprintMatches,
    bool SignatureVerifies,
    bool OfficialPinned,
    AdministratorTrustState AdministratorTrust,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>The conclusion of trust evaluation for one definition.</summary>
/// <param name="State">What verification concluded about the signature.</param>
/// <param name="Policy">Whether installation is denied regardless of that state.</param>
/// <param name="Expired">Whether the key was outside its validity interval when signed.</param>
public readonly record struct CatalogTrustEvaluation(
    CatalogSignatureState State,
    CatalogTrustPolicy Policy,
    bool Expired);

/// <summary>
/// Evaluates the trust of one catalog definition, exactly as section 6 specifies.
/// </summary>
/// <remarks>
/// Order matters and is deliberate: bytes first, then the key's identity, then who vouches
/// for it. An administrator decision is consulted only after the cryptography has already
/// succeeded, which is what makes "trust can never turn a digest mismatch or an invalid
/// signature into installable content" true by construction rather than by review.
/// </remarks>
public static class CatalogTrustEvaluator
{
    /// <summary>Evaluates a definition that published no signature envelope.</summary>
    /// <remarks>
    /// Unsigned content is a simple warning, never a denial. Nothing claimed authenticity,
    /// so nothing failed to prove it.
    /// </remarks>
    public static CatalogTrustEvaluation EvaluateUnsigned() =>
        new(CatalogSignatureState.NotSigned, CatalogTrustPolicy.Allow, Expired: false);

    /// <summary>Evaluates a definition that published a signature envelope.</summary>
    /// <param name="evidence">What is known about the key and the signature.</param>
    /// <param name="signedAtUtc">The envelope's creation time, which the validity interval is checked at.</param>
    /// <param name="officialSource">Whether the definition came from the official source.</param>
    public static CatalogTrustEvaluation EvaluateSigned(
        CatalogKeyEvidence evidence,
        DateTimeOffset signedAtUtc,
        bool officialSource)
    {
        // A definition that claims authenticity and cannot prove it is invalid, whatever the
        // reason: no key, wrong key, or bytes that do not verify.
        if (!evidence.Exists || !evidence.FingerprintMatches || !evidence.SignatureVerifies)
        {
            return new CatalogTrustEvaluation(
                CatalogSignatureState.InvalidSignature,
                CatalogTrustPolicy.Deny,
                Expired: false);
        }

        if (signedAtUtc < evidence.ValidFromUtc)
        {
            // A signature dated before its key existed is not merely expired; it is a claim
            // the key could not have made.
            return new CatalogTrustEvaluation(
                CatalogSignatureState.InvalidSignature,
                CatalogTrustPolicy.Deny,
                Expired: false);
        }

        var expired = evidence.ValidUntilUtc is DateTimeOffset validUntil && signedAtUtc > validUntil;

        var state = evidence.AdministratorTrust == AdministratorTrustState.Trusted || evidence.OfficialPinned
            ? CatalogSignatureState.TrustedSigned
            : CatalogSignatureState.UnknownSigned;

        // Revocation and explicit distrust deny, but they do not rewrite what the signature
        // was: history stays explainable, and only the policy changes.
        var policy = evidence.RevokedAtUtc is not null
            || evidence.AdministratorTrust == AdministratorTrustState.Distrusted
            || (expired && officialSource)
            ? CatalogTrustPolicy.Deny
            : CatalogTrustPolicy.Allow;

        return new CatalogTrustEvaluation(state, policy, expired);
    }
}
