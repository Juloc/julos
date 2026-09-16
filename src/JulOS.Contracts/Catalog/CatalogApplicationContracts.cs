using System.Text.Json;

namespace JulOS.Contracts.Catalog;

/// <summary>One cached version of one application, with what it was verified as.</summary>
/// <param name="Version">The version the source published.</param>
/// <param name="DefinitionDigest">The digest over the canonical definition, which a signature covers.</param>
/// <param name="SignatureState">What verification concluded about the signature.</param>
/// <param name="PublisherId">Who claimed to produce it, or null when unsigned.</param>
/// <param name="SignatureKeyId">Which key signed it, or null when unsigned.</param>
/// <param name="PublicKeyFingerprint">The fingerprint of that key, or null when unsigned.</param>
/// <param name="TrustAssessmentDigest">The digest binding the whole trust conclusion.</param>
/// <param name="SourceRevision">The refresh generation this version was cached in.</param>
/// <param name="CachedAtUtc">When it was cached.</param>
public sealed record CatalogApplicationVersionResponse(
    string Version,
    string DefinitionDigest,
    string SignatureState,
    string? PublisherId,
    string? SignatureKeyId,
    string? PublicKeyFingerprint,
    string TrustAssessmentDigest,
    int SourceRevision,
    DateTimeOffset CachedAtUtc);

/// <summary>One application as the cached catalog holds it.</summary>
/// <param name="CatalogSourceId">The source that published it.</param>
/// <param name="SourceIdentity">The stable identity that source claims.</param>
/// <param name="SourceDisplayName">The source name shown to administrators.</param>
/// <param name="SourceRefreshState">Whether the cached catalog is current.</param>
/// <param name="AppId">Application identity within the source.</param>
/// <param name="Versions">Every cached version, ordered by version text.</param>
/// <remarks>
/// Versions are listed rather than ranked. JulOS does not yet define how two version strings
/// compare — that is the update-policy question `APP-003` owns — and guessing an order here
/// would make "which version is newer" an answer the catalog format never gave.
/// </remarks>
public sealed record CatalogApplicationResponse(
    Guid CatalogSourceId,
    string SourceIdentity,
    string SourceDisplayName,
    string SourceRefreshState,
    string AppId,
    IReadOnlyList<CatalogApplicationVersionResponse> Versions);

/// <summary>One cached application version together with its complete definition.</summary>
/// <param name="CatalogSourceId">The source that published it.</param>
/// <param name="SourceIdentity">The stable identity that source claims.</param>
/// <param name="SourceRefreshState">Whether the cached catalog is current.</param>
/// <param name="AppId">Application identity within the source.</param>
/// <param name="Version">The version this definition is for.</param>
/// <param name="Definition">The canonical definition the digest was taken over.</param>
/// <param name="Trust">What this version was verified as.</param>
public sealed record CatalogApplicationDetailResponse(
    Guid CatalogSourceId,
    string SourceIdentity,
    string SourceRefreshState,
    string AppId,
    string Version,
    JsonElement Definition,
    CatalogApplicationVersionResponse Trust);
