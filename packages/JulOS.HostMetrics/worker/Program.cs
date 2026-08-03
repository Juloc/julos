using JulOS.PackageSdk;

using JulOS.HostMetrics.Worker;

return await PackageWorkerHost.RunAsync(
    new HostMetricsWorker(TimeProvider.System),
    args).ConfigureAwait(false);

namespace JulOS.HostMetrics.Worker
{
    internal sealed class HostMetricsWorker : IJulOsPackageWorker
    {
        private const string PackageId = "de.juloc.julos.hostmetrics";
        private readonly TimeProvider timeProvider;
        private PackageWorkerContext? context;
        private bool running;

        internal HostMetricsWorker(TimeProvider timeProvider)
        {
            this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public Task<PackageValidationResult> ValidateConfigurationAsync(
            IReadOnlyDictionary<string, string> configuration,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            cancellationToken.ThrowIfCancellationRequested();
            var issues = configuration.Keys
                .Select(key => new PackageValidationIssue(
                    "hostmetrics.configuration.unknown_field",
                    "Host Metrics does not accept package configuration fields.",
                    key,
                    Blocking: true))
                .ToArray();
            return Task.FromResult(new PackageValidationResult(issues.Length == 0, issues));
        }

        public Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(context.PackageId, PackageId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Host Metrics worker package identity is invalid.");
            }

            this.context = context;
            return Task.CompletedTask;
        }

        public Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PackageRegistration(
                [
                    new RegisteredApplication(
                        "host-metrics",
                        "app.hostmetrics.name",
                        "single-instance-per-user",
                        920,
                        680,
                        480,
                        360,
                        ["desktop", "tablet", "mobile"]),
                ],
                [
                    new RegisteredWidget(
                        "host-summary",
                        "widget.hostmetrics.summary.name",
                        "julos-host-metrics-widget",
                        ["small", "medium", "wide"],
                        "medium"),
                ],
                [],
                [
                    new RegisteredProblemCondition(
                        "agent-offline",
                        "warning",
                        "problem.hostmetrics.agent_offline"),
                    new RegisteredProblemCondition(
                        "metrics-stale",
                        "warning",
                        "problem.hostmetrics.metrics_stale"),
                ]));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this.context is null)
            {
                throw new InvalidOperationException("Host Metrics must be configured before start.");
            }

            this.running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.running = false;
            return Task.CompletedTask;
        }

        public Task<PackageHealthSnapshot> ReadHealthAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new PackageHealthSnapshot(
                this.running ? "healthy" : "stopped",
                this.timeProvider.GetUtcNow(),
                this.running ? null : "Host Metrics worker is stopped.",
                new Dictionary<string, decimal?>(StringComparer.Ordinal)));
        }
    }
}
