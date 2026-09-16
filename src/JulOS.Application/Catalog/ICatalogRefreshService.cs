using JulOS.Application.Operations;

namespace JulOS.Application.Catalog;

/// <summary>What one completed refresh achieved.</summary>
/// <param name="CatalogSourceId">The source that was refreshed.</param>
/// <param name="Succeeded">Whether the refresh replaced the cached catalog.</param>
/// <param name="SourceRevision">The refresh generation, or the previous one when it failed.</param>
/// <param name="SourceDigest">The immutable identity the refresh locked to, or null when it failed.</param>
/// <param name="EntryCount">How many entries the cache now holds for the source.</param>
/// <param name="FailureCode">The stable code the refresh failed with, or null.</param>
public sealed record CatalogRefreshResult(
    Guid CatalogSourceId,
    bool Succeeded,
    int? SourceRevision,
    string? SourceDigest,
    int EntryCount,
    string? FailureCode);

/// <summary>
/// Reads one catalog source and replaces its cached entries, or keeps the previous ones.
/// </summary>
/// <remarks>
/// A refresh is all-or-nothing by construction: everything is read and verified into memory
/// first, and the cached rows are replaced in one transaction only after the complete source
/// parsed. A partially readable source therefore leaves the last valid catalog in place with
/// an explicit stale marker, rather than leaving the installation serving half of two
/// catalogs.
/// </remarks>
public interface ICatalogRefreshService
{
    /// <summary>
    /// Queues a refresh of one source and returns the operation that owns it.
    /// </summary>
    /// <remarks>
    /// Requesting the same refresh twice while one is still queued or running returns that
    /// operation rather than starting a second one, so a caller retrying a request cannot
    /// make two refreshes race for the same cache.
    /// </remarks>
    /// <param name="catalogSourceId">Source to refresh.</param>
    /// <param name="ownerUserId">The authenticated administrator who asked.</param>
    /// <param name="correlationId">Correlation identifier of the calling request.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The source is missing, removed, disabled or unreadable.</exception>
    Task<OperationSnapshot> RequestAsync(
        Guid catalogSourceId,
        Guid ownerUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Refreshes one source.</summary>
    /// <param name="catalogSourceId">Source to refresh.</param>
    /// <param name="operationId">The operation that owns the work and any credential lease.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>What the refresh achieved. A failed refresh is a result, not an exception.</returns>
    /// <exception cref="CatalogFailureException">The source does not exist or is not refreshable.</exception>
    Task<CatalogRefreshResult> RefreshAsync(
        Guid catalogSourceId,
        Guid operationId,
        CancellationToken cancellationToken = default);
}
