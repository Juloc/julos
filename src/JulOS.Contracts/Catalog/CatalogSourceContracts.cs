namespace JulOS.Contracts.Catalog;

/// <summary>The stable catalog-source and publisher-key error codes from <c>docs/APPLICATION_CATALOG.md</c>.</summary>
public static class CatalogErrorCodes
{
    /// <summary>No catalog source with the requested identity exists.</summary>
    public const string SourceNotFound = "catalog.source_not_found";

    /// <summary>A catalog source field failed validation.</summary>
    public const string SourceInvalid = "catalog.source_invalid";

    /// <summary>The source is a tombstone and is not changed further.</summary>
    public const string SourceRemoved = "catalog.source_removed";

    /// <summary>Another live source already reads from the same location.</summary>
    public const string SourceDuplicate = "catalog.source_duplicate";

    /// <summary>No publisher key with the requested identity exists.</summary>
    public const string PublisherKeyNotFound = "catalog.publisher_key_not_found";

    /// <summary>A publisher key field or trust decision failed validation.</summary>
    public const string PublisherKeyInvalid = "catalog.publisher_key_invalid";
}

/// <summary>Where a catalog source gets its content from.</summary>
public static class CatalogSourceKindNames
{
    /// <summary>The built-in JulOS source.</summary>
    public const string Official = "official";

    /// <summary>A static versioned catalog served over HTTPS.</summary>
    public const string Https = "https";

    /// <summary>A public or private Git repository.</summary>
    public const string Git = "git";

    /// <summary>A catalog artifact stored in an OCI registry.</summary>
    public const string Oci = "oci";

    /// <summary>An administrator-managed local catalog.</summary>
    public const string Local = "local";

    /// <summary>Every source kind name.</summary>
    public static IReadOnlyList<string> All { get; } = [Official, Https, Git, Oci, Local];

    /// <summary>The kinds an administrator may add. The official source is built in.</summary>
    public static IReadOnlyList<string> Addable { get; } = [Https, Git, Oci, Local];
}

/// <summary>How much this installation trusts a source as a source.</summary>
public static class CatalogSourceTrustLevelNames
{
    /// <summary>The built-in source.</summary>
    public const string Official = "official";

    /// <summary>Explicitly trusted by an administrator.</summary>
    public const string AdministratorTrusted = "administrator-trusted";

    /// <summary>Added but not elevated.</summary>
    public const string Custom = "custom";

    /// <summary>Every trust-level name.</summary>
    public static IReadOnlyList<string> All { get; } = [Official, AdministratorTrusted, Custom];

    /// <summary>The trust levels an administrator may assign.</summary>
    /// <remarks>
    /// <c>official</c> is absent: only the built-in source is official, otherwise an
    /// administrator could mint a second one and inherit the key set that belongs to the
    /// real one.
    /// </remarks>
    public static IReadOnlyList<string> Assignable { get; } = [AdministratorTrusted, Custom];
}

/// <summary>What the last refresh of a source achieved.</summary>
public static class CatalogRefreshStateNames
{
    /// <summary>Never refreshed.</summary>
    public const string Never = "never";

    /// <summary>The cached catalog is the result of a successful refresh.</summary>
    public const string Fresh = "fresh";

    /// <summary>The last refresh failed and the previous valid catalog is still served.</summary>
    public const string Stale = "stale";
}

/// <summary>What verification concluded about one definition's signature.</summary>
/// <remarks>
/// The names are the ones the trust table in <c>docs/APPLICATION_CATALOG.md</c> section 6
/// uses. <c>unsigned</c> and <c>invalid-signature</c> are deliberately different values: an
/// artifact that claims authenticity and cannot prove it is a stronger claim than one that
/// never said so.
/// </remarks>
public static class CatalogSignatureStateNames
{
    /// <summary>Verified with a key that is official-pinned or administrator-trusted.</summary>
    public const string TrustedSigned = "trusted-signed";

    /// <summary>Cryptographically valid, but the key is not one this installation trusts.</summary>
    public const string UnknownSigned = "unknown-signed";

    /// <summary>No signature envelope was published for the definition.</summary>
    /// <remarks>Named NotSigned rather than Unsigned so it is not read as a numeric type.</remarks>
    public const string NotSigned = "unsigned";

    /// <summary>The definition claims authenticity and fails, or cannot be verified.</summary>
    public const string InvalidSignature = "invalid-signature";

    /// <summary>Every signature state name.</summary>
    public static IReadOnlyList<string> All { get; } =
        [TrustedSigned, UnknownSigned, NotSigned, InvalidSignature];
}

/// <summary>What an administrator decided about a publisher key.</summary>
public static class AdministratorTrustStateNames
{
    /// <summary>Seen, never decided.</summary>
    public const string Unknown = "unknown";

    /// <summary>Explicitly trusted by an administrator.</summary>
    public const string Trusted = "trusted";

    /// <summary>Explicitly distrusted by an administrator.</summary>
    public const string Distrusted = "distrusted";

    /// <summary>Every decision name.</summary>
    public static IReadOnlyList<string> All { get; } = [Unknown, Trusted, Distrusted];
}

/// <summary>Adds a catalog source.</summary>
/// <param name="Kind">One of <see cref="CatalogSourceKindNames.Addable"/>.</param>
/// <param name="DisplayName">The name shown to administrators.</param>
/// <param name="Location">The source location, interpreted by the adapter for its kind.</param>
/// <param name="AuthenticationSecretReferenceId">Secret reference holding private credentials, or null.</param>
/// <param name="TrustLevel">One of <see cref="CatalogSourceTrustLevelNames.Assignable"/>.</param>
public sealed record AddCatalogSourceRequest(
    string Kind,
    string DisplayName,
    string Location,
    Guid? AuthenticationSecretReferenceId,
    string TrustLevel);

/// <summary>Changes what an administrator may change about a catalog source.</summary>
/// <param name="DisplayName">The name shown to administrators.</param>
/// <param name="Location">The source location.</param>
/// <param name="AuthenticationSecretReferenceId">Secret reference holding private credentials, or null.</param>
/// <param name="TrustLevel">One of <see cref="CatalogSourceTrustLevelNames.Assignable"/>.</param>
/// <param name="Enabled">Whether the source is consulted at all.</param>
/// <param name="ExpectedRevision">The revision the caller last read.</param>
/// <remarks>
/// The kind is deliberately absent. Changing it would keep the identity installed
/// applications point at while changing what that identity means.
/// </remarks>
public sealed record UpdateCatalogSourceRequest(
    string DisplayName,
    string Location,
    Guid? AuthenticationSecretReferenceId,
    string TrustLevel,
    bool Enabled,
    int ExpectedRevision);

/// <summary>One catalog source as an administrator sees it.</summary>
/// <param name="CatalogSourceId">The source identity.</param>
/// <param name="Kind">Where the source gets its content from.</param>
/// <param name="DisplayName">The name shown to administrators.</param>
/// <param name="Location">The source location.</param>
/// <param name="AuthenticationSecretReferenceId">Secret reference holding private credentials, or null.</param>
/// <param name="TrustLevel">How much this installation trusts the source as a source.</param>
/// <param name="Enabled">Whether the source is consulted at all.</param>
/// <param name="RemovedAtUtc">When the source was removed, or null.</param>
/// <param name="LastSuccessfulRevision">The source revision of the last catalog that parsed completely.</param>
/// <param name="LastSuccessfulDigest">The immutable digest of that catalog.</param>
/// <param name="LastRefreshAtUtc">When the source was last refreshed, successfully or not.</param>
/// <param name="LastRefreshState">What that refresh achieved.</param>
/// <param name="LastFailureCode">The stable code of the last failure, or null.</param>
/// <param name="Revision">Optimistic-concurrency revision.</param>
/// <remarks>
/// Only the identifier of an authentication secret is returned. The credential itself is
/// never part of any response.
/// </remarks>
public sealed record CatalogSourceResponse(
    Guid CatalogSourceId,
    string Kind,
    string DisplayName,
    string Location,
    Guid? AuthenticationSecretReferenceId,
    string TrustLevel,
    bool Enabled,
    DateTimeOffset? RemovedAtUtc,
    int? LastSuccessfulRevision,
    string? LastSuccessfulDigest,
    DateTimeOffset? LastRefreshAtUtc,
    string LastRefreshState,
    string? LastFailureCode,
    int Revision);

/// <summary>One publisher key observed in a catalog source.</summary>
/// <param name="CatalogPublisherKeyId">The record identity.</param>
/// <param name="CatalogSourceId">The source that published the key.</param>
/// <param name="PublisherId">The publisher the key belongs to.</param>
/// <param name="KeyId">The publisher-assigned key identity.</param>
/// <param name="Algorithm">The signature algorithm the key is for.</param>
/// <param name="PublicKeyFingerprint">The calculated SPKI fingerprint, as <c>sha256:...</c>.</param>
/// <param name="ValidFromUtc">Start of the key validity interval.</param>
/// <param name="ValidUntilUtc">End of the key validity interval, or null when open-ended.</param>
/// <param name="RevokedAtUtc">When the key was revoked, or null.</param>
/// <param name="AdministratorTrustState">What an administrator decided about this key.</param>
/// <param name="AdministratorDecisionByUserId">Who made that decision, when there is one.</param>
/// <param name="AdministratorDecisionAtUtc">When that decision was made, when there is one.</param>
/// <param name="FirstObservedSourceRevision">The source revision the key was first seen in.</param>
/// <param name="LastObservedSourceRevision">The source revision the key was most recently seen in.</param>
/// <param name="Revision">Optimistic-concurrency revision.</param>
/// <remarks>
/// The fingerprint is returned rather than the key bytes: it is what an administrator
/// compares against the value the publisher states, and it is what the decision is about.
/// </remarks>
public sealed record CatalogPublisherKeyResponse(
    Guid CatalogPublisherKeyId,
    Guid CatalogSourceId,
    string PublisherId,
    string KeyId,
    string Algorithm,
    string PublicKeyFingerprint,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidUntilUtc,
    DateTimeOffset? RevokedAtUtc,
    string AdministratorTrustState,
    Guid? AdministratorDecisionByUserId,
    DateTimeOffset? AdministratorDecisionAtUtc,
    int FirstObservedSourceRevision,
    int LastObservedSourceRevision,
    int Revision);

/// <summary>Records an administrator decision about one publisher key.</summary>
/// <param name="AdministratorTrustState">One of <see cref="AdministratorTrustStateNames.All"/>.</param>
/// <param name="ExpectedRevision">The revision the caller last read.</param>
public sealed record SetPublisherKeyTrustRequest(string AdministratorTrustState, int ExpectedRevision);
