using System.Security.Cryptography;
using System.Text.Json;

using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.PackageSdk;

namespace JulOS.AdaptiveBrowser.Worker;

/// <summary>Owns server-side Adaptive Browser runtime policy without changing the legacy Browser package.</summary>
public sealed class AdaptiveBrowserWorker : IJulOsPackageWorker, IJulOsPackageCommandHandler
{
    private const string PackageId = "de.juloc.julos.adaptive-browser";
    private const string PresentationProtocol = "browser-stream";
    private const int PresentationPort = 8080;
    private const int DefaultIdleTimeoutMinutes = 30;
    private const int MaximumSessionSeconds = 86400;
    private static readonly RuntimeResourceLimits RuntimeLimits = new(1536, 2m, 256);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider timeProvider;
    private PackageWorkerContext? context;
    private AdaptiveBrowserPolicy? policy;
    private bool running;

    public AdaptiveBrowserWorker(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<PackageValidationResult> ValidateConfigurationAsync(
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var allowedKeys = new HashSet<string>(
            ["idleTimeoutMinutes", "allowedNetworks", "defaultNetwork", "runtimeImage"],
            StringComparer.Ordinal);
        var issues = configuration.Keys
            .Where(key => !allowedKeys.Contains(key))
            .Select(key => new PackageValidationIssue(
                "adaptive-browser.configuration.unknown",
                "Adaptive Browser configuration contains an unsupported field.",
                key,
                Blocking: true))
            .ToList();

        if (configuration.TryGetValue("idleTimeoutMinutes", out var timeout)
            && (!int.TryParse(timeout, out var minutes) || minutes is < 1 or > 1440))
        {
            issues.Add(new PackageValidationIssue(
                "adaptive-browser.configuration.timeout",
                "idleTimeoutMinutes must be from 1 through 1440.",
                "idleTimeoutMinutes",
                Blocking: true));
        }

        if (configuration.TryGetValue("runtimeImage", out var image)
            && !string.IsNullOrWhiteSpace(image)
            && !IsDigestPinnedImage(image.Trim()))
        {
            issues.Add(new PackageValidationIssue(
                "adaptive-browser.configuration.runtime_image",
                "runtimeImage must be an immutable lowercase sha256 image reference.",
                "runtimeImage",
                Blocking: true));
        }

        try
        {
            _ = AdaptiveBrowserPolicy.FromConfiguration(configuration);
        }
        catch (ArgumentException exception)
        {
            issues.Add(new PackageValidationIssue(
                "adaptive-browser.configuration.network",
                exception.Message,
                "allowedNetworks",
                Blocking: true));
        }

        return Task.FromResult(new PackageValidationResult(issues.Count == 0, issues));
    }

    public Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.PackageId, PackageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Adaptive Browser worker package identity is invalid.");
        }

        this.context = context;
        this.policy = AdaptiveBrowserPolicy.FromConfiguration(context.Configuration);
        return Task.CompletedTask;
    }

    public Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PackageRegistration(
            [
                new RegisteredApplication(
                    "adaptive-browser",
                    "app.adaptive-browser.name",
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
                    "server-session-start-failed",
                    "error",
                    "problem.adaptive-browser.server_session_start_failed"),
            ]));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (this.context is null || this.policy is null)
        {
            throw new InvalidOperationException("Adaptive Browser must be configured before start.");
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
            this.running ? null : "Adaptive Browser worker is stopped.",
            new Dictionary<string, decimal?>(StringComparer.Ordinal)
            {
                ["allowedNetworkCount"] = this.policy?.AllowedNetworks.Count,
            }));
    }

    public Task<PackageWorkerCommandResult> InvokeCommandAsync(
        PackageWorkerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        if (!this.running || this.context is null || this.policy is null)
        {
            return Task.FromResult(Failure(
                "adaptive-browser.worker_unavailable",
                "Adaptive Browser worker is not ready."));
        }

        return Task.FromResult(command.Name switch
        {
            InteractiveSessionWorkerCommands.ResolvePlan => this.ResolvePlan(command),
            _ => Failure(
                "adaptive-browser.command_unsupported",
                "Adaptive Browser worker command is not supported."),
        });
    }

    private PackageWorkerCommandResult ResolvePlan(PackageWorkerCommand command)
    {
        if (this.context is null || this.policy is null)
        {
            return Failure("adaptive-browser.worker_unavailable", "Adaptive Browser worker is not ready.");
        }

        ResolveInteractiveSessionPlanRequest? input;
        AdaptiveBrowserSessionRequest? request;
        try
        {
            input = command.Payload.Deserialize<ResolveInteractiveSessionPlanRequest>(JsonOptions);
            request = input?.Request.Request.Deserialize<AdaptiveBrowserSessionRequest>(JsonOptions);
        }
        catch (JsonException)
        {
            return Failure("adaptive-browser.request_invalid", "Adaptive Browser session request is invalid.");
        }

        if (input is null || input.OwnerUserId == Guid.Empty || request is null)
        {
            return Failure("adaptive-browser.request_invalid", "Adaptive Browser session request is invalid.");
        }
        if (!string.Equals(request.ExecutionMode, "server", StringComparison.Ordinal))
        {
            return Failure(
                "adaptive-browser.execution_mode_invalid",
                "Only server execution creates an Adaptive Browser runtime session.");
        }
        if (!TryReadHttpUrl(request.InitialUrl, out var url))
        {
            return Failure("adaptive-browser.url_invalid", "Adaptive Browser URL must use HTTP or HTTPS.");
        }

        var runtimeImage = ReadRuntimeImage(this.context.Configuration);
        if (runtimeImage is null)
        {
            return Failure(
                "adaptive-browser.runtime_not_configured",
                "Adaptive Browser runtime image is not configured.");
        }

        var network = string.IsNullOrWhiteSpace(request.Network)
            ? this.policy.DefaultNetwork
            : request.Network.Trim();
        if (network is null || !this.policy.AllowedNetworks.Contains(network, StringComparer.Ordinal))
        {
            return Failure(
                "adaptive-browser.network_denied",
                "The requested Adaptive Browser runtime network is not allowlisted.");
        }

        var width = Math.Clamp(request.ViewportWidth ?? 1280, 320, 3840);
        var height = Math.Clamp(request.ViewportHeight ?? 800, 240, 2160);
        var scale = Math.Clamp(request.DeviceScaleFactor ?? 1m, 0.5m, 3m);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JULOS_START_URL"] = url.AbsoluteUri,
            ["JULOS_BROWSER_VIEWPORT_WIDTH"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["JULOS_BROWSER_VIEWPORT_HEIGHT"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["JULOS_BROWSER_DEVICE_SCALE_FACTOR"] = scale.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var plan = new InteractiveSessionRuntimePlan(
            this.context.PackageVersion,
            runtimeImage,
            RuntimeLimits,
            environment,
            network,
            [],
            PresentationProtocol,
            PresentationPort,
            new InteractiveSessionCredential("JULOS_BROWSER_STREAM_TOKEN", token),
            new RemoteViewportContract(width, height, scale),
            ReadIdleTimeoutSeconds(this.context.Configuration),
            MaximumSessionSeconds);
        return Success(plan);
    }

    private static int ReadIdleTimeoutSeconds(IReadOnlyDictionary<string, string> configuration)
    {
        var minutes = configuration.TryGetValue("idleTimeoutMinutes", out var configured)
            && int.TryParse(configured, out var parsed)
            ? parsed
            : DefaultIdleTimeoutMinutes;
        return checked(minutes * 60);
    }

    private static string? ReadRuntimeImage(IReadOnlyDictionary<string, string> configuration)
    {
        if (!configuration.TryGetValue("runtimeImage", out var configured)
            || string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }
        var image = configured.Trim();
        return IsDigestPinnedImage(image) ? image : null;
    }

    private static bool IsDigestPinnedImage(string image)
    {
        const string marker = "@sha256:";
        var index = image.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 1)
        {
            return false;
        }
        var digest = image[(index + marker.Length)..];
        return digest.Length == 64
            && digest.All(character => char.IsAsciiHexDigit(character) && !char.IsAsciiLetterUpper(character));
    }

    private static bool TryReadHttpUrl(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https"
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

    private sealed record AdaptiveBrowserSessionRequest(
        string InitialUrl,
        string ExecutionMode,
        string? Network,
        int? ViewportWidth,
        int? ViewportHeight,
        decimal? DeviceScaleFactor);

    private sealed record AdaptiveBrowserPolicy(
        IReadOnlyList<string> AllowedNetworks,
        string? DefaultNetwork)
    {
        internal static AdaptiveBrowserPolicy FromConfiguration(IReadOnlyDictionary<string, string> configuration)
        {
            var allowed = configuration.TryGetValue("allowedNetworks", out var value)
                ? value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [];
            if (allowed.Length == 0
                || allowed.Distinct(StringComparer.Ordinal).Count() != allowed.Length
                || allowed.Any(network => !ValidNetwork(network)))
            {
                throw new ArgumentException("allowedNetworks must contain one or more unique Runtime Manager network names.");
            }

            var defaultNetwork = configuration.TryGetValue("defaultNetwork", out var configuredDefault)
                && !string.IsNullOrWhiteSpace(configuredDefault)
                ? configuredDefault.Trim()
                : allowed[0];
            if (!allowed.Contains(defaultNetwork, StringComparer.Ordinal))
            {
                throw new ArgumentException("defaultNetwork must be present in allowedNetworks.");
            }
            return new AdaptiveBrowserPolicy(allowed, defaultNetwork);
        }

        private static bool ValidNetwork(string value) =>
            value.Length is > 0 and <= 64
            && char.IsAsciiLetterOrDigit(value[0])
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }
}
