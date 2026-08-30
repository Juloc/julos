using JulOS.AdaptiveBrowser.Worker;
using JulOS.PackageSdk;

return await PackageWorkerHost.RunAsync(
    new AdaptiveBrowserWorker(TimeProvider.System),
    args).ConfigureAwait(false);
