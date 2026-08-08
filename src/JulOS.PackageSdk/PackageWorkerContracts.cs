using System.Text.Json;

namespace JulOS.PackageSdk;

/// <summary>Configuration and identity supplied to one package worker instance.</summary>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="PackageVersion">Installed package version.</param>
/// <param name="ServerEndpoint">Authenticated control-plane endpoint.</param>
/// <param name="InstanceId">Worker instance identity.</param>
/// <param name="Configuration">Validated non-secret configuration values.</param>
/// <param name="GrantedCapabilities">Capabilities granted to the worker.</param>
public sealed record PackageWorkerContext(
    string PackageId,
    string PackageVersion,
    Uri ServerEndpoint,
    string InstanceId,
    IReadOnlyDictionary<string, string> Configuration,
    IReadOnlyList<string> GrantedCapabilities);

/// <summary>One configuration validation issue.</summary>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Message">Caller-safe explanation.</param>
/// <param name="Field">Affected field when applicable.</param>
/// <param name="Blocking">Whether activation must stop.</param>
public sealed record PackageValidationIssue(
    string Code,
    string Message,
    string? Field,
    bool Blocking);

/// <summary>Result of package configuration validation.</summary>
/// <param name="Valid">Whether configuration may be applied.</param>
/// <param name="Issues">All discovered issues.</param>
public sealed record PackageValidationResult(
    bool Valid,
    IReadOnlyList<PackageValidationIssue> Issues);

/// <summary>One observed package worker health state.</summary>
/// <param name="Status">Stable health status.</param>
/// <param name="ObservedAtUtc">Observation time.</param>
/// <param name="Detail">Optional caller-safe detail.</param>
/// <param name="Measurements">Bounded package-owned measurements.</param>
public sealed record PackageHealthSnapshot(
    string Status,
    DateTimeOffset ObservedAtUtc,
    string? Detail,
    IReadOnlyDictionary<string, decimal?> Measurements);

/// <summary>Worker registrations verified against the signed manifest.</summary>
/// <param name="Applications">Registered applications.</param>
/// <param name="Widgets">Registered widgets.</param>
/// <param name="Capabilities">Registered capabilities.</param>
/// <param name="ProblemConditions">Registered problem conditions.</param>
public sealed record PackageRegistration(
    IReadOnlyList<RegisteredApplication> Applications,
    IReadOnlyList<RegisteredWidget> Widgets,
    IReadOnlyList<RegisteredCapability> Capabilities,
    IReadOnlyList<RegisteredProblemCondition> ProblemConditions);

/// <summary>One worker application registration.</summary>
public sealed record RegisteredApplication(
    string StableKey,
    string DisplayNameKey,
    string InstancePolicy,
    int DefaultWidth,
    int DefaultHeight,
    int MinimumWidth,
    int MinimumHeight,
    IReadOnlyList<string> Viewports);

/// <summary>One worker widget registration.</summary>
public sealed record RegisteredWidget(
    string StableKey,
    string DisplayNameKey,
    string ElementName,
    IReadOnlyList<string> Sizes,
    string DefaultSize);

/// <summary>One worker capability registration.</summary>
/// <param name="Name">Capability identity.</param>
/// <param name="ContractVersion">Supported contract version.</param>
public sealed record RegisteredCapability(
    string Name,
    string ContractVersion);

/// <summary>One package-owned problem condition registration.</summary>
/// <param name="ConditionKey">Stable condition key.</param>
/// <param name="Severity">Declared severity.</param>
/// <param name="TitleKey">Localized title resource key.</param>
public sealed record RegisteredProblemCondition(
    string ConditionKey,
    string Severity,
    string TitleKey);

/// <summary>One private control-plane command delivered only to the owning package worker.</summary>
/// <param name="Name">Package-defined bounded command name.</param>
/// <param name="Payload">Package-defined JSON payload.</param>
public sealed record PackageWorkerCommand(
    string Name,
    JsonElement Payload);

/// <summary>Result returned by a package-owned private worker command.</summary>
/// <param name="Succeeded">Whether the command was accepted.</param>
/// <param name="ErrorCode">Stable caller-safe package error code.</param>
/// <param name="ErrorDetail">Caller-safe error detail.</param>
/// <param name="Payload">Package-defined successful payload.</param>
public sealed record PackageWorkerCommandResult(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorDetail,
    JsonElement Payload);

/// <summary>Optional private command handler for package-owned policy and data operations.</summary>
public interface IJulOsPackageCommandHandler
{
    /// <summary>Handles one control-plane command without exposing the worker transport to packages.</summary>
    Task<PackageWorkerCommandResult> InvokeCommandAsync(
        PackageWorkerCommand command,
        CancellationToken cancellationToken);
}

/// <summary>The complete lifecycle contract hosted by every package worker.</summary>
public interface IJulOsPackageWorker
{
    /// <summary>Validates configuration without applying it.</summary>
    Task<PackageValidationResult> ValidateConfigurationAsync(
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken);

    /// <summary>Applies a previously validated worker context.</summary>
    Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken);

    /// <summary>Returns registrations for verification against the manifest.</summary>
    Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken);

    /// <summary>Starts package work.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Stops package work.</summary>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Reads the current worker health state.</summary>
    Task<PackageHealthSnapshot> ReadHealthAsync(CancellationToken cancellationToken);
}

/// <summary>Runs worker calls with a bounded deadline and caller cancellation.</summary>
public sealed class PackageWorkerDeadline
{
    private readonly TimeSpan timeout;

    /// <summary>Creates a worker-call deadline.</summary>
    public PackageWorkerDeadline(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        this.timeout = timeout;
    }

    /// <summary>Runs a value-returning operation within the deadline.</summary>
    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(this.timeout);
        return await operation(deadline.Token).ConfigureAwait(false);
    }

    /// <summary>Runs an operation within the deadline.</summary>
    public async Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(this.timeout);
        await operation(deadline.Token).ConfigureAwait(false);
    }
}
