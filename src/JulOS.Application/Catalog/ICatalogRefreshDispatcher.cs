namespace JulOS.Application.Catalog;

/// <summary>One requested refresh and the operation that owns it.</summary>
/// <param name="CatalogSourceId">Source to refresh.</param>
/// <param name="OperationId">The durable operation the caller polls.</param>
public sealed record CatalogRefreshRequest(Guid CatalogSourceId, Guid OperationId);

/// <summary>
/// Hands a requested refresh to the executor that runs it.
/// </summary>
/// <remarks>
/// A refresh reads a whole remote catalog, so it does not run inside the request that asked
/// for it. The request returns the durable operation immediately and the caller follows that
/// instead of holding a connection open for however long the source takes.
/// </remarks>
public interface ICatalogRefreshDispatcher
{
    /// <summary>Queues one refresh.</summary>
    /// <param name="request">What to refresh and under which operation.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <exception cref="CatalogFailureException">The queue is full.</exception>
    ValueTask EnqueueAsync(CatalogRefreshRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads queued refreshes until the host stops.</summary>
    /// <param name="cancellationToken">Host shutdown.</param>
    IAsyncEnumerable<CatalogRefreshRequest> ReadAllAsync(CancellationToken cancellationToken);
}
