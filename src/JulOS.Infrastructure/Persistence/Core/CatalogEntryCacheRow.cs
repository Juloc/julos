using JulOS.Domain.Catalog;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>
/// One cached catalog entry: the definition a refresh accepted and what it was verified with.
/// </summary>
/// <remarks>
/// <para>
/// The cache is the catalog this installation serves. A refresh replaces the rows of one
/// source in a single transaction or replaces none of them, which is what makes a failed
/// refresh keep the previous catalog rather than a half-updated one.
/// </para>
/// <para>
/// <c>Definition</c> is not part of the record `docs/DATA_AND_API_CONTRACTS.md` lists. It is
/// stored because the cache has to be able to serve the last valid catalog without going
/// back to a source that may be unreachable; the bytes are the canonical form the
/// <c>DefinitionDigest</c> was taken over, so they carry no state the digest does not cover.
/// </para>
/// </remarks>
internal sealed class CatalogEntryCacheRow
{
    internal Guid Id { get; set; }

    internal Guid CatalogSourceId { get; set; }

    internal required string AppId { get; set; }

    internal required string Version { get; set; }

    /// <summary>The refresh generation this entry was cached in.</summary>
    internal int SourceRevision { get; set; }

    /// <summary>The immutable identity of the source content the refresh locked to.</summary>
    internal required string SourceDigest { get; set; }

    /// <summary>The digest over the canonical definition, which the signature covers.</summary>
    internal required string DefinitionDigest { get; set; }

    /// <summary>The canonical definition bytes, as UTF-8 JSON text.</summary>
    internal required string Definition { get; set; }

    internal string? PublisherId { get; set; }

    internal string? SignatureKeyId { get; set; }

    internal string? PublicKeyFingerprint { get; set; }

    internal CatalogSignatureState SignatureState { get; set; }

    /// <summary>The digest binding the whole trust conclusion an administrator would be shown.</summary>
    internal required string TrustAssessmentDigest { get; set; }

    internal DateTimeOffset CachedAtUtc { get; set; }

    internal int Revision { get; set; }
}
