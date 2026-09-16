using System.Text.Json;

namespace JulOS.Domain.Catalog;

/// <summary>
/// Everything about trust that an administrator was shown for one cached definition, and the
/// digest that binds it.
/// </summary>
/// <remarks>
/// <para>
/// A deployment stores this digest so that "what did the administrator actually approve" has
/// an answer later, when the current trust state may differ. Revocation or a changed
/// decision never rewrites an installed snapshot; the difference shows up as a different
/// digest in update Preview, which is exactly the signal that a fresh acknowledgement is
/// needed.
/// </para>
/// <para>
/// The digest covers the conclusion and the evidence it came from together. Covering only
/// the conclusion would let the same `unknown-signed` value stand for a different key, and
/// the approval would silently carry over to content a different publisher signed.
/// </para>
/// </remarks>
/// <param name="SignatureState">What verification concluded.</param>
/// <param name="Policy">Whether installation is denied regardless of that state.</param>
/// <param name="Expired">Whether the key was outside its validity interval when it signed.</param>
/// <param name="PublisherId">Who claimed to produce the definition, or null when unsigned.</param>
/// <param name="KeyId">Which key signed it, or null when unsigned.</param>
/// <param name="PublicKeyFingerprint">The fingerprint of that key, or null when unsigned.</param>
/// <param name="OfficialPinned">Whether the key is part of the built-in release configuration.</param>
/// <param name="AdministratorTrust">What an administrator had decided about the key.</param>
public sealed record CatalogTrustAssessment(
    CatalogSignatureState SignatureState,
    CatalogTrustPolicy Policy,
    bool Expired,
    string? PublisherId,
    string? KeyId,
    string? PublicKeyFingerprint,
    bool OfficialPinned,
    AdministratorTrustState AdministratorTrust)
{
    /// <summary>The lowercase SHA-256 over the canonical JSON form of this assessment.</summary>
    public string Digest()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["administratorTrust"] = this.AdministratorTrust.ToString(),
            ["expired"] = this.Expired,
            ["keyId"] = this.KeyId,
            ["officialPinned"] = this.OfficialPinned,
            ["policy"] = this.Policy.ToString(),
            ["publicKeyFingerprint"] = this.PublicKeyFingerprint,
            ["publisherId"] = this.PublisherId,
            ["signatureState"] = this.SignatureState.ToString(),
        });

        using var document = JsonDocument.Parse(json);
        return CatalogCanonicalJson.DefinitionDigest(document.RootElement);
    }

    /// <summary>Builds the assessment for a definition that published no signature.</summary>
    public static CatalogTrustAssessment ForUnsignedDefinition(CatalogTrustEvaluation evaluation) => new(
        evaluation.State,
        evaluation.Policy,
        evaluation.Expired,
        PublisherId: null,
        KeyId: null,
        PublicKeyFingerprint: null,
        OfficialPinned: false,
        AdministratorTrustState.Unknown);
}
