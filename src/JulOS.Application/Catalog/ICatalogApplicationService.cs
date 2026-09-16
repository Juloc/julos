using JulOS.Contracts.Catalog;

namespace JulOS.Application.Catalog;

/// <summary>Reads the cached catalog this installation is serving.</summary>
/// <remarks>
/// <para>
/// Reads never reach a source. What a refresh accepted is what is served, which is what keeps
/// a catalog readable while its source is unreachable — and why every response carries the
/// refresh state of the source it came from, so a stale catalog is visibly stale rather than
/// quietly old.
/// </para>
/// <para>
/// Removed sources are excluded. Their tombstones keep installed applications resolvable, but
/// a source an administrator removed is not one this installation offers to install from.
/// </para>
/// </remarks>
public interface ICatalogApplicationService
{
    /// <summary>Lists the cached applications.</summary>
    /// <param name="catalogSourceId">Restrict to one source, or null for all of them.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The named source does not exist.</exception>
    Task<IReadOnlyList<CatalogApplicationResponse>> ListAsync(
        Guid? catalogSourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one cached application version and its complete definition.</summary>
    /// <param name="catalogSourceId">The source that published it.</param>
    /// <param name="appId">Application identity within the source.</param>
    /// <param name="version">The version to read, or null for the only cached one.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">
    /// The source, the application or the version is not cached, or several versions are
    /// cached and none was named.
    /// </exception>
    Task<CatalogApplicationDetailResponse> ReadAsync(
        Guid catalogSourceId,
        string appId,
        string? version,
        CancellationToken cancellationToken = default);
}
