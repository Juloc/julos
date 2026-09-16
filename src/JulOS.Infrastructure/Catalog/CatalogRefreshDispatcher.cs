using System.Threading.Channels;

using JulOS.Application.Catalog;
using JulOS.Contracts.Catalog;

namespace JulOS.Infrastructure.Catalog;

/// <summary>
/// The in-process queue of requested catalog refreshes.
/// </summary>
/// <remarks>
/// Bounded and non-blocking on purpose. A queue that grows without limit turns a source that
/// never answers into unbounded memory on this host, and a queue that blocks the writer
/// would make the HTTP request wait for exactly the work it is trying not to wait for. A
/// full queue is therefore refused, and the administrator sees that rather than a request
/// that appears to have been accepted.
/// </remarks>
internal sealed class CatalogRefreshDispatcher : ICatalogRefreshDispatcher
{
    /// <summary>How many refreshes may be waiting at once.</summary>
    private const int Capacity = 64;

    private readonly Channel<CatalogRefreshRequest> channel =
        Channel.CreateBounded<CatalogRefreshRequest>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ValueTask EnqueueAsync(
        CatalogRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return this.channel.Writer.TryWrite(request)
            ? ValueTask.CompletedTask
            : throw new CatalogFailureException(
                CatalogErrorCodes.SourceInvalid,
                CatalogFailureReason.Invalid,
                "Too many catalog refreshes are already queued.");
    }

    public IAsyncEnumerable<CatalogRefreshRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        this.channel.Reader.ReadAllAsync(cancellationToken);
}
