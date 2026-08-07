using JulOS.PackageSdk;

namespace JulOS.Browser.Worker;

/// <summary>Registers isolated Browser sessions and owns Browser profile policy.</summary>
public sealed class BrowserWorker : IJulOsPackageWorker
{
    private const string PackageId = "de.juloc.julos.browser";
    private readonly TimeProvider timeProvider;
    private PackageWorkerContext? context;
    private BrowserProfilePolicy? profilePolicy;
    private BrowserProfileStore? profileStore;
    private bool running;

    /// <summary>Creates the Browser worker.</summary>
    /// <param name="timeProvider">Authoritative clock.</param>
    public BrowserWorker(TimeProvider timeProvider)
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
        var allowed = new HashSet<string>(
            ["idleTimeoutMinutes", "allowDownloads", "allowedNetworks", "defaultNetwork"],
            StringComparer.Ordinal);
        var issues = configuration.Keys
            .Where(key => !allowed.Contains(key))
            .Select(key => new PackageValidationIssue(
                "browser.configuration.unknown",
                "Browser configuration contains an unsupported field.",
                key,
                Blocking: true))
            .ToList();
        if (configuration.TryGetValue("idleTimeoutMinutes", out var timeout)
            && (!int.TryParse(timeout, out var minutes) || minutes is < 1 or > 1440))
        {
            issues.Add(new PackageValidationIssue(
                "browser.configuration.timeout",
                "idleTimeoutMinutes must be from 1 through 1440.",
                "idleTimeoutMinutes",
                Blocking: true));
        }
        if (configuration.TryGetValue("allowDownloads", out var downloads)
            && !bool.TryParse(downloads, out _))
        {
            issues.Add(new PackageValidationIssue(
                "browser.configuration.downloads",
                "allowDownloads must be true or false.",
                "allowDownloads",
                Blocking: true));
        }

        try
        {
            _ = BrowserProfilePolicy.FromConfiguration(configuration);
        }
        catch (ArgumentException exception)
        {
            issues.Add(new PackageValidationIssue(
                "browser.configuration.network",
                exception.Message,
                "allowedNetworks",
                Blocking: true));
        }

        return Task.FromResult(new PackageValidationResult(issues.Count == 0, issues));
    }

    /// <inheritdoc />
    public Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.PackageId, PackageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Browser worker package identity is invalid.");
        }

        this.context = context;
        this.profilePolicy = BrowserProfilePolicy.FromConfiguration(context.Configuration);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PackageRegistration(
            [
                new RegisteredApplication(
                    "browser",
                    "app.browser.name",
                    "multiple-instances",
                    1180,
                    780,
                    640,
                    420,
                    ["desktop", "tablet", "mobile"]),
            ],
            [],
            [],
            [
                new RegisteredProblemCondition(
                    "session-start-failed",
                    "error",
                    "problem.browser.session_start_failed"),
                new RegisteredProblemCondition(
                    "session-expired",
                    "information",
                    "problem.browser.session_expired"),
            ]));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.context is null || this.profilePolicy is null)
        {
            throw new InvalidOperationException("Browser must be configured before start.");
        }

        this.profileStore = CreateProfileStore();
        await this.profileStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        this.running = true;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.running = false;
        this.profileStore = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PackageHealthSnapshot> ReadHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PackageHealthSnapshot(
            this.running ? "healthy" : "stopped",
            this.timeProvider.GetUtcNow(),
            this.running ? null : "Browser worker is stopped.",
            new Dictionary<string, decimal?>(StringComparer.Ordinal)
            {
                ["allowedNetworkCount"] = this.profilePolicy?.AllowedNetworkCount,
            }));
    }

    private static BrowserProfileStore CreateProfileStore()
    {
        var connectionString = Environment.GetEnvironmentVariable("JULOS_PACKAGE_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Browser package database environment is unavailable.");
        }

        var provider = Environment.GetEnvironmentVariable("JULOS_PACKAGE_DATABASE_PROVIDER");
        if (string.IsNullOrWhiteSpace(provider))
        {
            var schema = Environment.GetEnvironmentVariable("JULOS_PACKAGE_DATABASE_SCHEMA");
            provider = string.Equals(schema, "main", StringComparison.Ordinal) ? "sqlite" : "postgresql";
        }

        return new BrowserProfileStore(provider, connectionString);
    }
}
