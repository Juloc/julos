using JulOS.PackageSdk;

return await PackageWorkerHost.RunAsync(
    new ReferencePackageWorker(TimeProvider.System),
    args).ConfigureAwait(false);

internal sealed class ReferencePackageWorker : IJulOsPackageWorker
{
    private readonly TimeProvider timeProvider;
    private PackageWorkerContext? context;
    private bool running;
    private bool faultMode;

    internal ReferencePackageWorker(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<PackageValidationResult> ValidateConfigurationAsync(
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<PackageValidationIssue>();
        foreach (var key in configuration.Keys)
        {
            if (key is not ("message" or "faultMode"))
            {
                issues.Add(new PackageValidationIssue(
                    "reference.configuration.unknown_field",
                    $"Unknown configuration field '{key}'.",
                    key,
                    Blocking: true));
            }
        }

        if (configuration.TryGetValue("message", out var message) && message.Length > 256)
        {
            issues.Add(new PackageValidationIssue(
                "reference.configuration.message_too_long",
                "Message must contain at most 256 characters.",
                "message",
                Blocking: true));
        }

        if (configuration.TryGetValue("faultMode", out var faultValue)
            && !bool.TryParse(faultValue, out _))
        {
            issues.Add(new PackageValidationIssue(
                "reference.configuration.fault_mode_invalid",
                "faultMode must be true or false.",
                "faultMode",
                Blocking: true));
        }

        return Task.FromResult(new PackageValidationResult(
            issues.All(issue => !issue.Blocking),
            issues));
    }

    public Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        this.context = context;
        this.faultMode = context.Configuration.TryGetValue("faultMode", out var value)
            && bool.TryParse(value, out var enabled)
            && enabled;
        return Task.CompletedTask;
    }

    public Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PackageRegistration(
            [
                new RegisteredApplication(
                    "reference",
                    "app.reference.name",
                    "single-instance-per-user",
                    720,
                    520,
                    360,
                    280,
                    ["desktop", "tablet", "mobile"]),
            ],
            [
                new RegisteredWidget(
                    "status",
                    "widget.reference.status.name",
                    "julos-reference-widget",
                    ["small", "medium"],
                    "small"),
            ],
            [new RegisteredCapability("reference.echo", "1.0.0")],
            [new RegisteredProblemCondition(
                "reference.intentional_fault",
                "warning",
                "problem.reference.intentional_fault.title")]));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.context is null)
        {
            throw new InvalidOperationException("Reference worker must be configured before start.");
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
        var status = !this.running ? "stopped" : this.faultMode ? "error" : "healthy";
        var detail = this.faultMode ? "Intentional reference-package fault mode is enabled." : null;
        return Task.FromResult(new PackageHealthSnapshot(
            status,
            this.timeProvider.GetUtcNow(),
            detail,
            new Dictionary<string, decimal?>
            {
                ["reference.counter"] = this.running ? 1 : 0,
            }));
    }
}
