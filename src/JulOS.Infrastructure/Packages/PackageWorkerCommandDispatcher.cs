using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Packages;

/// <summary>Dispatches a bounded private command to one already-running package worker.</summary>
public interface IPackageWorkerCommandDispatcher
{
    /// <summary>Invokes one private command on the selected active package worker.</summary>
    Task<PackageWorkerCommandResult> InvokeAsync(
        string packageId,
        PackageWorkerCommand command,
        CancellationToken cancellationToken);
}
