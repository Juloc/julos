using JulOS.Application.Catalog;
using JulOS.Application.Operations;

namespace JulOS.Server.Catalog;

/// <summary>Runs queued catalog refreshes and records the result on their operation.</summary>
/// <remarks>
/// A refresh that fails is a completed operation with a stable failure code, not a crashed
/// worker: the source records the same code, and the next request is processed normally.
/// </remarks>
internal sealed class CatalogRefreshWorker : BackgroundService
{
    private readonly ICatalogRefreshDispatcher queue;
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<CatalogRefreshWorker> logger;

    public CatalogRefreshWorker(
        ICatalogRefreshDispatcher queue,
        IServiceScopeFactory scopes,
        ILogger<CatalogRefreshWorker> logger)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in this.queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await this.RunAsync(request, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CatalogRefreshRequest request, CancellationToken cancellationToken)
    {
        await using var scope = this.scopes.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationService>();
        var refresh = scope.ServiceProvider.GetRequiredService<ICatalogRefreshService>();

        try
        {
            _ = await operations.MarkRunningAsync(request.OperationId, cancellationToken).ConfigureAwait(false);

            var result = await refresh
                .RefreshAsync(request.CatalogSourceId, request.OperationId, cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded)
            {
                _ = await operations.MarkSucceededAsync(request.OperationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _ = await operations.MarkFailedAsync(
                    request.OperationId,
                    result.FailureCode ?? CatalogRefreshException.SourceUnavailable,
                    "The catalog source could not be refreshed; the previous catalog is still served.",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting down. The operation stays running and the next start
            // finds it exactly as it was, rather than being reported as a failure it is not.
            throw;
        }
        catch (Exception exception)
        {
            CatalogRefreshLog.Failed(this.logger, request.CatalogSourceId, exception);
            await this.FailAsync(operations, request, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task FailAsync(
        IOperationService operations,
        CatalogRefreshRequest request,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception is CatalogFailureException failure
            ? failure.Code
            : CatalogRefreshException.SourceUnavailable;

        try
        {
            // The exception text may name a host or a path, so only the stable code and a
            // fixed sentence reach the operation the caller reads.
            _ = await operations.MarkFailedAsync(
                request.OperationId,
                code,
                "The catalog refresh did not complete.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationFailureException bookkeeping)
        {
            CatalogRefreshLog.Failed(this.logger, request.CatalogSourceId, bookkeeping);
        }
    }
}

/// <summary>Log messages of the catalog refresh worker.</summary>
internal static partial class CatalogRefreshLog
{
    [LoggerMessage(
        EventId = 6100,
        Level = LogLevel.Error,
        Message = "Refreshing catalog source {CatalogSourceId} failed.")]
    internal static partial void Failed(ILogger logger, Guid catalogSourceId, Exception exception);
}
