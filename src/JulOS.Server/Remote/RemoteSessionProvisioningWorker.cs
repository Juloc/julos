using JulOS.Application.Remote;

namespace JulOS.Server.Remote;

/// <summary>Continuously resumes durable Remote runtime provisioning outside client requests.</summary>
internal sealed class RemoteSessionProvisioningWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly Action<ILogger, Exception?> ReconciliationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(6110, nameof(ReconciliationFailed)),
        "Remote provisioning reconciliation failed.");

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RemoteSessionProvisioningWorker> logger;

    public RemoteSessionProvisioningWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RemoteSessionProvisioningWorker> logger)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await this.ReconcileAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(Interval, this.timeProvider, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<IRemoteSessionProvisioningReconciler>();
            _ = await reconciler.ReconcilePendingAsync(10, cancellationToken).ConfigureAwait(false);
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
