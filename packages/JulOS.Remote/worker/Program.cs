using JulOS.PackageSdk;
using JulOS.Remote.Worker;

return await PackageWorkerHost.RunAsync(
    new RemoteWorker(TimeProvider.System),
    args).ConfigureAwait(false);
