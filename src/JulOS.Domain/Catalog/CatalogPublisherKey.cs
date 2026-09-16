using JulOS.Domain.Primitives;

namespace JulOS.Domain.Catalog;

/// <summary>The generated identity of one observed catalog publisher key.</summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct CatalogPublisherKeyId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}

/// <summary>
/// One public key a catalog source published, and what this installation decided about it.
/// </summary>
/// <remarks>
/// <para>
/// Trust is persisted as evidence rather than recomputed into history. The key records what
/// was observed and when, and separately what an administrator decided; a later decision
/// changes future evaluation and never rewrites what an installed application was verified
/// with.
/// </para>
/// <para>
/// A key identity binds immutably to one public key. A source that reuses a key ID with
/// different bytes is not rotating a key, it is replacing one silently, and the refusal for
/// that is the whole reason this record exists.
/// </para>
/// </remarks>
public sealed class CatalogPublisherKey
{
    private CatalogPublisherKey(
        CatalogPublisherKeyId id,
        Guid catalogSourceId,
        string publisherId,
        string keyId,
        string algorithm,
        string publicKeySpki,
        string publicKeyFingerprint,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validUntilUtc,
        int firstObservedSourceRevision)
    {
        this.Id = id;
        this.CatalogSourceId = catalogSourceId;
        this.PublisherId = publisherId;
        this.KeyId = keyId;
        this.Algorithm = algorithm;
        this.PublicKeySpki = publicKeySpki;
        this.PublicKeyFingerprint = publicKeyFingerprint;
        this.ValidFromUtc = validFromUtc;
        this.ValidUntilUtc = validUntilUtc;
        this.FirstObservedSourceRevision = firstObservedSourceRevision;
        this.LastObservedSourceRevision = firstObservedSourceRevision;
        this.AdministratorTrustState = AdministratorTrustState.Unknown;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this record.</summary>
    public CatalogPublisherKeyId Id { get; }

    /// <summary>The source that published the key.</summary>
    public Guid CatalogSourceId { get; }

    /// <summary>The publisher the key belongs to.</summary>
    public string PublisherId { get; }

    /// <summary>The publisher-assigned key identity. Immutable against its bytes.</summary>
    public string KeyId { get; }

    /// <summary>The signature algorithm the key is for.</summary>
    public string Algorithm { get; }

    /// <summary>Base64 SubjectPublicKeyInfo bytes.</summary>
    public string PublicKeySpki { get; }

    /// <summary>The calculated SPKI fingerprint, as <c>sha256:...</c>.</summary>
    public string PublicKeyFingerprint { get; }

    /// <summary>Start of the key validity interval.</summary>
    public DateTimeOffset ValidFromUtc { get; }

    /// <summary>End of the key validity interval, or null when open-ended.</summary>
    public DateTimeOffset? ValidUntilUtc { get; }

    /// <summary>When the key was revoked, or null.</summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>What an administrator decided about this key.</summary>
    public AdministratorTrustState AdministratorTrustState { get; private set; }

    /// <summary>Who made that decision, when there is one.</summary>
    public Guid? AdministratorDecisionByUserId { get; private set; }

    /// <summary>When that decision was made, when there is one.</summary>
    public DateTimeOffset? AdministratorDecisionAtUtc { get; private set; }

    /// <summary>The source revision the key was first seen in.</summary>
    public int FirstObservedSourceRevision { get; }

    /// <summary>The source revision the key was most recently seen in.</summary>
    public int LastObservedSourceRevision { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Records a key observed in a source refresh.</summary>
    /// <exception cref="DomainRuleViolationException">A required identity or fingerprint is missing.</exception>
    public static CatalogPublisherKey Observe(
        CatalogPublisherKeyId id,
        Guid catalogSourceId,
        string publisherId,
        string keyId,
        string algorithm,
        string publicKeySpki,
        string publicKeyFingerprint,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validUntilUtc,
        int sourceRevision)
    {
        if (catalogSourceId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "catalog.publisher_key_invalid",
                "A publisher key belongs to a catalog source.");
        }

        if (validUntilUtc is DateTimeOffset until && until <= validFromUtc)
        {
            throw new DomainRuleViolationException(
                "catalog.publisher_key_invalid",
                "A key validity interval ends after it begins.");
        }

        return new CatalogPublisherKey(
            id,
            catalogSourceId,
            RequireIdentity(publisherId, "publisher"),
            RequireIdentity(keyId, "key"),
            RequireAlgorithm(algorithm),
            RequireValue(publicKeySpki, "public key"),
            RequireFingerprint(publicKeyFingerprint),
            validFromUtc,
            validUntilUtc,
            sourceRevision);
    }

    /// <summary>
    /// Confirms that a key observed again is the same key.
    /// </summary>
    /// <remarks>
    /// A reused key identity carrying different bytes fails the whole refresh rather than
    /// this one entry. Accepting it would let a source replace the key that vouches for
    /// everything it has ever published, silently and in one step.
    /// </remarks>
    /// <exception cref="DomainRuleViolationException">The bytes or fingerprint changed.</exception>
    public void ObserveAgain(string publicKeySpki, string publicKeyFingerprint, int sourceRevision)
    {
        if (!string.Equals(this.PublicKeySpki, publicKeySpki, StringComparison.Ordinal)
            || !string.Equals(this.PublicKeyFingerprint, publicKeyFingerprint, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "catalog.publisher_key_conflict",
                $"Key '{this.KeyId}' was published with different bytes; a key identity binds to one key.");
        }

        if (sourceRevision > this.LastObservedSourceRevision)
        {
            this.LastObservedSourceRevision = sourceRevision;
        }
    }

    /// <summary>Records an administrator decision about this key.</summary>
    /// <param name="state">The decision.</param>
    /// <param name="decidedByUserId">The authenticated administrator, for a decision other than unknown.</param>
    /// <param name="decidedAtUtc">When the decision was made.</param>
    /// <exception cref="DomainRuleViolationException">A decision is recorded without its author.</exception>
    public void Decide(AdministratorTrustState state, Guid? decidedByUserId, DateTimeOffset decidedAtUtc)
    {
        if (state == AdministratorTrustState.Unknown)
        {
            // Clearing a decision clears who made it: an undecided key must not keep
            // pointing at an administrator who no longer stands behind it.
            this.AdministratorTrustState = AdministratorTrustState.Unknown;
            this.AdministratorDecisionByUserId = null;
            this.AdministratorDecisionAtUtc = null;
            this.Revision = this.Revision.Next();
            return;
        }

        if (decidedByUserId is not Guid author || author == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "catalog.publisher_key_invalid",
                "A trust decision records the administrator who made it.");
        }

        this.AdministratorTrustState = state;
        this.AdministratorDecisionByUserId = author;
        this.AdministratorDecisionAtUtc = decidedAtUtc;
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Records that the key was revoked.
    /// </summary>
    /// <remarks>
    /// Revocation denies future installs and updates. It deliberately does not delete
    /// running applications or rewrite installed locks: what was verified remains what was
    /// verified, and the difference surfaces in update Preview instead.
    /// </remarks>
    public void Revoke(DateTimeOffset revokedAtUtc)
    {
        if (this.RevokedAtUtc is not null)
        {
            return;
        }

        this.RevokedAtUtc = revokedAtUtc;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Restores a persisted key without advancing its revision.</summary>
    public static CatalogPublisherKey Restore(
        CatalogPublisherKeyId id,
        Guid catalogSourceId,
        string publisherId,
        string keyId,
        string algorithm,
        string publicKeySpki,
        string publicKeyFingerprint,
        DateTimeOffset validFromUtc,
        DateTimeOffset? validUntilUtc,
        DateTimeOffset? revokedAtUtc,
        AdministratorTrustState administratorTrustState,
        Guid? administratorDecisionByUserId,
        DateTimeOffset? administratorDecisionAtUtc,
        int firstObservedSourceRevision,
        int lastObservedSourceRevision,
        Revision revision) =>
        new(
            id,
            catalogSourceId,
            publisherId,
            keyId,
            algorithm,
            publicKeySpki,
            publicKeyFingerprint,
            validFromUtc,
            validUntilUtc,
            firstObservedSourceRevision)
        {
            RevokedAtUtc = revokedAtUtc,
            AdministratorTrustState = administratorTrustState,
            AdministratorDecisionByUserId = administratorDecisionByUserId,
            AdministratorDecisionAtUtc = administratorDecisionAtUtc,
            LastObservedSourceRevision = lastObservedSourceRevision,
            Revision = revision,
        };

    private static string RequireIdentity(string value, string what)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > 128
            ? throw new DomainRuleViolationException(
                "catalog.publisher_key_invalid",
                $"A {what} identity has 1 to 128 characters.")
            : trimmed;
    }

    private static string RequireValue(string value, string what) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new DomainRuleViolationException(
                "catalog.publisher_key_invalid",
                $"A {what} is required.")
            : value;

    private static string RequireAlgorithm(string algorithm) =>
        string.Equals(algorithm, CatalogSignatureInput.Algorithm, StringComparison.Ordinal)
            ? algorithm
            : throw new DomainRuleViolationException(
                "catalog.signature_algorithm_unsupported",
                $"'{algorithm}' is not a supported catalog signature algorithm.");

    private static string RequireFingerprint(string fingerprint)
    {
        const string prefix = "sha256:";
        var valid = fingerprint is not null
            && fingerprint.StartsWith(prefix, StringComparison.Ordinal)
            && CatalogSignatureInput.IsLowercaseSha256(fingerprint[prefix.Length..]);

        return valid
            ? fingerprint!
            : throw new DomainRuleViolationException(
                "catalog.publisher_key_invalid",
                "A public key fingerprint is 'sha256:' followed by 64 lowercase hexadecimal characters.");
    }
}
