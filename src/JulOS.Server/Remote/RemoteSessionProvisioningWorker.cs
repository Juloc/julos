using JulOS.Application.Remote;

namespace JulOS.Server.Remote;

/// <summary>Resumes durable Remote runtime provisioning outside client requests.</summary>
internal sealed class RemoteSessionProvisioningWorker : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly Action<ILogger, Exception?> ReconciliationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(6110, nameof(ReconciliationFailed)),
        "Remote provisioning reconciliation failed.");

    private readonly IServiceScopeFactory scopeFactory;
    private readonly IRemoteSessionProvisioningSignal signal;
    private readonly ILogger<RemoteSessionProvisioningWorker> logger;

    public RemoteSessionProvisioningWorker(
        IServiceScopeFactory scopeFactory,
        IRemoteSessionProvisioningSignal signal,
        ILogger<RemoteSessionProvisioningWorker> logger)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.signal = signal ?? throw new ArgumentNullException(nameof(signal));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recover sessions that were requested or provisioning when the previous
        // Server process stopped. Subsequent work is signalled by capability create.
        await this.ReconcileUntilDrainedAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await this.signal.WaitAsync(stoppingToken).ConfigureAwait(false);
            await this.ReconcileUntilDrainedAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileUntilDrainedAsync(CancellationToken cancellationToken)
    {
        try
        {
            RemoteProvisioningReconciliationResult result;
            do
            {
                await using var scope = this.scopeFactory.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<IRemoteSessionProvisioningReconciler>();
                result = await reconciler.ReconcilePendingAsync(BatchSize, cancellationToken).ConfigureAwait(false);
            }
            while (result.Examined == BatchSize && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ReconciliationFailed(this.logger, exception);
        }
    }
}
