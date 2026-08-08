using System.Text.Json;

using JulOS.Contracts.Browser;
using JulOS.PackageSdk;

namespace JulOS.Browser.Worker;

/// <summary>Registers isolated Browser sessions and owns Browser profile policy.</summary>
public sealed class BrowserWorker : IJulOsPackageWorker, IJulOsPackageCommandHandler
{
    private const string PackageId = "de.juloc.julos.browser";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

        this.profileStore = BrowserProfileStore.FromEnvironment();
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

    /// <inheritdoc />
    public async Task<PackageWorkerCommandResult> InvokeCommandAsync(
        PackageWorkerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!this.running || this.profilePolicy is null || this.profileStore is null)
        {
            return Failure("browser.worker_unavailable", "Browser worker is not ready.");
        }
        if (!string.Equals(command.Name, BrowserWorkerCommands.ResolveSessionPlan, StringComparison.Ordinal))
        {
            return Failure("browser.command_unsupported", "Browser worker command is not supported.");
        }

        ResolveBrowserSessionPlanRequest? input;
        try
        {
            input = command.Payload.Deserialize<ResolveBrowserSessionPlanRequest>(JsonOptions);
        }
        catch (JsonException)
        {
            return Failure("browser.request_invalid", "Browser session request is invalid.");
        }
        if (input is null || input.OwnerUserId == Guid.Empty || input.Request is null)
        {
            return Failure("browser.request_invalid", "Browser session request is invalid.");
        }

        if (!TryReadHttpUrl(input.Request.InitialUrl, out var requestedUrl))
        {
            return Failure("browser.url_invalid", "Browser URL must use HTTP or HTTPS.");
        }

        if (string.Equals(input.Request.ProfileMode, BrowserSessionProfileModes.Temporary, StringComparison.Ordinal))
        {
            if (input.Request.ProfileId is not null || this.profilePolicy.DefaultNetwork is null)
            {
                return Failure(
                    "browser.profile_invalid",
                    "Temporary Browser session requires no profile and a configured default network.");
            }
            return Success(new BrowserSessionRuntimePlan(
                requestedUrl.AbsoluteUri,
                this.profilePolicy.DefaultNetwork,
                BrowserSessionProfileModes.Temporary,
                null,
                null));
        }

        if (input.Request.ProfileId is not Guid profileId || profileId == Guid.Empty)
        {
            return Failure("browser.profile_invalid", "Retained Browser session requires a profile.");
        }

        var profile = await this.profileStore
            .ReadProfileAsync(input.OwnerUserId, profileId, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null)
        {
            return Failure("browser.profile_not_found", "Browser profile was not found.");
        }

        var expectedMode = profile.Mode switch
        {
            BrowserProfileMode.Persistent => BrowserSessionProfileModes.Persistent,
            BrowserProfileMode.Application => BrowserSessionProfileModes.Application,
            _ => string.Empty,
        };
        if (!string.Equals(input.Request.ProfileMode, expectedMode, StringComparison.Ordinal))
        {
            return Failure("browser.profile_mode_mismatch", "Browser profile mode does not match the stored profile.");
        }

        var networks = await this.profileStore.ListNetworkProfilesAsync(cancellationToken).ConfigureAwait(false);
        var network = networks.SingleOrDefault(item =>
            string.Equals(item.Key, profile.NetworkProfileKey, StringComparison.Ordinal));
        if (network is null)
        {
            return Failure("browser.network_not_found", "Browser network profile was not found.");
        }
        try
        {
            _ = this.profilePolicy.CreateNetworkProfile(
                network.Key,
                network.RuntimeNetwork,
                network.ProxySecretReferenceId);
        }
        catch (InvalidOperationException)
        {
            return Failure("browser.network_denied", "Browser profile network is no longer allowed.");
        }

        var startUrl = profile.Mode == BrowserProfileMode.Application
            ? profile.StartUrl
            : requestedUrl;
        if (startUrl is null || !TryReadHttpUrl(startUrl.AbsoluteUri, out var validatedUrl))
        {
            return Failure("browser.url_invalid", "Browser profile URL is invalid.");
        }

        var storage = BrowserProfilePolicy.RuntimeStorage(profile);
        return Success(new BrowserSessionRuntimePlan(
            validatedUrl.AbsoluteUri,
            network.RuntimeNetwork,
            expectedMode,
            profile.ProfileId,
            storage.VolumeName));
    }

    private static bool TryReadHttpUrl(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme is Uri.UriSchemeHttp or Uri.UriSchemeHttps
            && string.IsNullOrEmpty(parsed.UserInfo))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static PackageWorkerCommandResult Success<T>(T payload) =>
        new(true, null, null, JsonSerializer.SerializeToElement(payload, JsonOptions));

    private static PackageWorkerCommandResult Failure(string code, string detail) =>
        new(false, code, detail, JsonSerializer.SerializeToElement(new { }, JsonOptions));
}
