using JulOS.Browser.Worker;
using JulOS.PackageSdk;

return await PackageWorkerHost.RunAsync(
    new BrowserWorker(TimeProvider.System),
    args).ConfigureAwait(false);
