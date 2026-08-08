using JulOS.Application.Remote;

namespace JulOS.Server.Remote;

/// <summary>Periodically expires stale Remote sessions and removes terminal runtimes.</summary>
internal sealed class RemoteSessionLifecycleWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly Action<ILogger, int, Exception?> CleanupFailures = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(6101, nameof(CleanupFailures)),
        "Remote lifecycle reconciliation left {FailureCount} runtime cleanup failures.");
    private static readonly Action<ILogger, Exception?> ReconciliationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(6102, nameof(ReconciliationFailed)),
        "Remote lifecycle reconciliation failed.");
    private static readonly Action<ILogger, int, Exception?> InteractiveCleanupFailures = LoggerMessage.Define<int>(
        LogLevel.Warning,
        new EventId(6103, nameof(InteractiveCleanupFailures)),
        "Interactive-session reconciliation left {FailureCount} cleanup failures.");

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<RemoteSessionLifecycleWorker> logger;

    public RemoteSessionLifecycleWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RemoteSessionLifecycleWorker> logger)
    {
        this.scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, this.timeProvider, stoppingToken).ConfigureAwait(false);
            await this.ReconcileAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = this.scopeFactory.CreateAsyncScope();
            var lifecycle = scope.ServiceProvider.GetRequiredService<IRemoteSessionLifecycleService>();
            var result = await lifecycle.ReconcileDueAsync(100, cancellationToken).ConfigureAwait(false);
            if (result.CleanupFailures > 0)
            {
                CleanupFailures(this.logger, result.CleanupFailures, null);
            }

            var interactive = scope.ServiceProvider.GetRequiredService<IInteractiveSessionCleanupService>();
            var interactiveResult = await interactive.ReconcileAsync(100, cancellationToken).ConfigureAwait(false);
            if (interactiveResult.Failures > 0)
            {
                InteractiveCleanupFailures(this.logger, interactiveResult.Failures, null);
            }
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
