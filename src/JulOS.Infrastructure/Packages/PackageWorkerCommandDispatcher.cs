using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Packages;

/// <summary>Dispatches a bounded private command to one already-running package worker.</summary>
internal interface IPackageWorkerCommandDispatcher
{
    Task<PackageWorkerCommandResult> InvokeAsync(
        string packageId,
        PackageWorkerCommand command,
        CancellationToken cancellationToken);
}
