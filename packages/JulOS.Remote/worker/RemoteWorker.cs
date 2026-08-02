using JulOS.PackageSdk;

namespace JulOS.Remote.Worker;

/// <summary>Registers the protocol-neutral Remote application and status widget.</summary>
public sealed class RemoteWorker : IJulOsPackageWorker
{
    private const string PackageId = "de.juloc.julos.remote";
    private readonly TimeProvider timeProvider;
    private PackageWorkerContext? context;
    private bool running;

    /// <summary>Creates the Remote package worker.</summary>
    public RemoteWorker(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public Task<PackageValidationResult> ValidateConfigurationAsync(
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = new HashSet<string>(["idleTimeoutMinutes", "maximumSessionMinutes"], StringComparer.Ordinal);
        var issues = configuration.Keys
            .Where(key => !allowed.Contains(key))
            .Select(key => new PackageValidationIssue(
                "remote.configuration.unknown",
                "Remote configuration contains an unsupported field.",
                key,
                Blocking: true))
            .ToList();
        ValidateRange(configuration, "idleTimeoutMinutes", 1, 1440, issues);
        ValidateRange(configuration, "maximumSessionMinutes", 5, 10080, issues);
        return Task.FromResult(new PackageValidationResult(issues.Count == 0, issues));
    }

    /// <inheritdoc />
    public Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.PackageId, PackageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Remote worker package identity is invalid.");
        }
        this.context = context;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PackageRegistration(
            [
                new RegisteredApplication(
                    "remote",
                    "app.remote.name",
                    "single-instance-per-target",
                    1100,
                    760,
                    640,
                    420,
                    ["desktop", "tablet", "mobile"]),
            ],
            [
                new RegisteredWidget(
                    "remote-sessions",
                    "widget.remote.sessions.name",
                    "julos-remote-widget",
                    ["small", "medium"],
                    "small"),
            ],
            [],
            [
                new RegisteredProblemCondition(
                    "session-disconnected",
                    "warning",
                    "problem.remote.session_disconnected"),
                new RegisteredProblemCondition(
                    "certificate-untrusted",
                    "error",
                    "problem.remote.certificate_untrusted"),
            ]));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.context is null)
        {
            throw new InvalidOperationException("Remote must be configured before start.");
        }
        this.running = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.running = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PackageHealthSnapshot> ReadHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PackageHealthSnapshot(
            this.running ? "healthy" : "stopped",
            this.timeProvider.GetUtcNow(),
            this.running ? null : "Remote worker is stopped.",
            new Dictionary<string, decimal?>(StringComparer.Ordinal)));
    }

    private static void ValidateRange(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        int minimum,
        int maximum,
        ICollection<PackageValidationIssue> issues)
    {
        if (configuration.TryGetValue(key, out var value)
            && (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum))
        {
            issues.Add(new PackageValidationIssue(
                "remote.configuration.range",
                $"{key} must be from {minimum} through {maximum}.",
                key,
                Blocking: true));
        }
    }
}
