namespace JulOS.PackageSdk;

public sealed record PackageWorkerContext(
    string PackageId,
    string PackageVersion,
    Uri ServerEndpoint,
    string InstanceId,
    IReadOnlyDictionary<string, string> Configuration,
    IReadOnlyList<string> GrantedCapabilities);

public sealed record PackageValidationIssue(
    string Code,
    string Message,
    string? Field,
    bool Blocking);

public sealed record PackageValidationResult(
    bool Valid,
    IReadOnlyList<PackageValidationIssue> Issues);

public sealed record PackageHealthSnapshot(
    string Status,
    DateTimeOffset ObservedAtUtc,
    string? Detail,
    IReadOnlyDictionary<string, decimal?> Measurements);

public sealed record PackageRegistration(
    IReadOnlyList<RegisteredApplication> Applications,
    IReadOnlyList<RegisteredWidget> Widgets,
    IReadOnlyList<RegisteredCapability> Capabilities,
    IReadOnlyList<RegisteredProblemCondition> ProblemConditions);

public sealed record RegisteredApplication(
    string StableKey,
    string DisplayNameKey,
    string InstancePolicy,
    int DefaultWidth,
    int DefaultHeight,
    int MinimumWidth,
    int MinimumHeight,
    IReadOnlyList<string> Viewports);

public sealed record RegisteredWidget(
    string StableKey,
    string DisplayNameKey,
    string ElementName,
    IReadOnlyList<string> Sizes,
    string DefaultSize);

public sealed record RegisteredCapability(
    string Name,
    string ContractVersion);

public sealed record RegisteredProblemCondition(
    string ConditionKey,
    string Severity,
    string TitleKey);

/// <summary>The complete lifecycle contract hosted by every package worker.</summary>
public interface IJulOsPackageWorker
{
    Task<PackageValidationResult> ValidateConfigurationAsync(
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken);

    Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken);

    Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task<PackageHealthSnapshot> ReadHealthAsync(CancellationToken cancellationToken);
}

public sealed class PackageWorkerDeadline
{
    private readonly TimeSpan timeout;

    public PackageWorkerDeadline(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        this.timeout = timeout;
    }

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(this.timeout);
        return await operation(deadline.Token).ConfigureAwait(false);
    }

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
