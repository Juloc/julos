using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

using JulOS.Application.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Npgsql;

namespace JulOS.Infrastructure.Packages;

/// <summary>Starts signed package process workers and supervises their bounded lifecycle protocol.</summary>
internal sealed class ProcessPackageWorkerSupervisor : IPackageWorkerSupervisor, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, WorkerSession> sessions = new(StringComparer.Ordinal);
    private readonly string packageRoot;
    private readonly Uri serverEndpoint;
    private readonly CoreDatabaseProvider databaseProvider;
    private readonly string administrativeConnectionString;

    internal ProcessPackageWorkerSupervisor(
        string packageRoot,
        Uri serverEndpoint,
        CoreDatabaseProvider databaseProvider,
        string administrativeConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(serverEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(administrativeConnectionString);
        this.packageRoot = Path.GetFullPath(packageRoot);
        this.serverEndpoint = serverEndpoint;
        this.databaseProvider = databaseProvider;
        this.administrativeConnectionString = administrativeConnectionString;
    }

    public async Task<PackageValidationResult> ValidateAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(configuration);
        if (manifest.Runtime.Kind == "none")
        {
            return new PackageValidationResult(true, []);
        }
        EnsureProcessRuntime(manifest);
        await using var session = StartSession(manifest, database: null);
        return await session.RequestAsync<PackageValidationResult>(
            "validate",
            configuration,
            Deadline(manifest),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task StartAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, string> configuration,
        PackageDatabaseIdentity database,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(database);
        if (manifest.Runtime.Kind == "none")
        {
            return;
        }
        EnsureProcessRuntime(manifest);
        if (this.sessions.ContainsKey(manifest.PackageId))
        {
            throw Failure("package.worker_already_running", "Package worker is already running.");
        }

        var session = StartSession(manifest, database);
        try
        {
            var timeout = Deadline(manifest);
            var context = new PackageWorkerContext(
                manifest.PackageId,
                manifest.Version,
                this.serverEndpoint,
                Guid.NewGuid().ToString("N"),
                configuration,
                manifest.Capabilities
                    .Where(capability => capability.Direction == "requires")
                    .Select(capability => capability.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            await session.RequestAsync("configure", context, timeout, cancellationToken).ConfigureAwait(false);
            var registration = await session.RequestAsync<PackageRegistration>(
                "register",
                new { },
                timeout,
                cancellationToken).ConfigureAwait(false);
            VerifyRegistration(manifest, registration);
            await session.RequestAsync("start", new { }, timeout, cancellationToken).ConfigureAwait(false);
            var health = await session.RequestAsync<PackageHealthSnapshot>(
                "health",
                new { },
                timeout,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(health.Status, "healthy", StringComparison.Ordinal))
            {
                throw Failure("package.worker_unhealthy", "Package worker did not become healthy.");
            }
            if (!this.sessions.TryAdd(manifest.PackageId, session))
            {
                throw Failure("package.worker_already_running", "Package worker is already running.");
            }
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (!this.sessions.TryRemove(packageId, out var session))
        {
            return;
        }
        await using (session.ConfigureAwait(false))
        {
            await session.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var active = this.sessions.ToArray();
        this.sessions.Clear();
        foreach (var pair in active)
        {
            await pair.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    private WorkerSession StartSession(PackageManifest manifest, PackageDatabaseIdentity? database)
    {
        var entryPoint = ResolveEntryPoint(manifest);
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(entryPoint)!,
        };
        start.ArgumentList.Add(entryPoint);
        start.ArgumentList.Add(PackageWorkerHost.StandardIoSwitch);
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["JULOS_PACKAGE_ID"] = manifest.PackageId;
        start.Environment["JULOS_PACKAGE_VERSION"] = manifest.Version;
        if (database is not null)
        {
            start.Environment["JULOS_PACKAGE_DATABASE"] = PackageConnectionString(database);
            start.Environment["JULOS_PACKAGE_DATABASE_PROVIDER"] = database.Provider;
            start.Environment["JULOS_PACKAGE_DATABASE_SCHEMA"] = database.Schema;
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw Failure("package.worker_start_failed", "Package worker process did not start.");
            }
            return new WorkerSession(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private string ResolveEntryPoint(PackageManifest manifest)
    {
        var declared = manifest.Runtime.EntryPoint
            ?? throw Failure("package.worker_entrypoint_missing", "Package worker entry point is missing.");
        if (Path.IsPathRooted(declared)
            || declared.Contains('\\')
            || declared.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw Failure("package.worker_entrypoint_invalid", "Package worker entry point is invalid.");
        }

        var versionRoot = Path.GetFullPath(Path.Combine(
            this.packageRoot,
            manifest.PackageId,
            "versions",
            manifest.Version));
        var entryPoint = Path.GetFullPath(Path.Combine(versionRoot, declared.Replace('/', Path.DirectorySeparatorChar)));
        if (!entryPoint.StartsWith(versionRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(entryPoint))
        {
            throw Failure("package.worker_entrypoint_missing", "Package worker entry point does not exist.");
        }
        return entryPoint;
    }

    private string PackageConnectionString(PackageDatabaseIdentity database)
    {
        if (this.databaseProvider == CoreDatabaseProvider.Sqlite)
        {
            if (!string.Equals(database.Provider, "sqlite", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(database.ConnectionString))
            {
                throw Failure("package.database_identity_invalid", "SQLite package database identity is invalid.");
            }
            return database.ConnectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(this.administrativeConnectionString)
        {
            Username = database.Role,
            Password = database.Password,
            SearchPath = database.Schema + ",pg_catalog",
            IncludeErrorDetail = false,
            Pooling = true,
            ApplicationName = "JulOS package " + database.PackageId,
        };
        return builder.ConnectionString;
    }

    private static TimeSpan Deadline(PackageManifest manifest) =>
        TimeSpan.FromSeconds(manifest.Runtime.StartupTimeoutSeconds);

    private static void EnsureProcessRuntime(PackageManifest manifest)
    {
        if (manifest.Runtime.Kind == "container")
        {
            throw Failure(
                "package.container_runtime_not_configured",
                "Container package workers require the Runtime Manager transport.");
        }
        if (manifest.Runtime.Kind != "process")
        {
            throw Failure("package.runtime_invalid", "Package runtime kind is invalid.");
        }
    }

    private static void VerifyRegistration(PackageManifest manifest, PackageRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var declaredApplications = manifest.Applications.Select(application => application.StableKey).ToHashSet(StringComparer.Ordinal);
        var registeredApplications = registration.Applications.Select(application => application.StableKey).ToHashSet(StringComparer.Ordinal);
        var declaredWidgets = manifest.Widgets.Select(widget => widget.StableKey).ToHashSet(StringComparer.Ordinal);
        var registeredWidgets = registration.Widgets.Select(widget => widget.StableKey).ToHashSet(StringComparer.Ordinal);
        var declaredCapabilities = manifest.Capabilities
            .Where(capability => capability.Direction == "provides")
            .Select(capability => capability.Name)
            .ToHashSet(StringComparer.Ordinal);
        var registeredCapabilities = registration.Capabilities.Select(capability => capability.Name).ToHashSet(StringComparer.Ordinal);
        if (!declaredApplications.SetEquals(registeredApplications)
            || !declaredWidgets.SetEquals(registeredWidgets)
            || !declaredCapabilities.SetEquals(registeredCapabilities))
        {
            throw Failure(
                "package.worker_registration_mismatch",
                "Package worker registration does not match the signed manifest.");
        }

        foreach (var widget in registration.Widgets)
        {
            var declared = manifest.Widgets.Single(item => item.StableKey == widget.StableKey);
            if (!string.Equals(declared.ElementName, widget.ElementName, StringComparison.Ordinal))
            {
                throw Failure(
                    "package.worker_registration_mismatch",
                    "Package worker widget registration does not match the signed manifest.");
            }
        }
    }

    private static PackageManagementException Failure(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    private sealed class WorkerSession : IAsyncDisposable
    {
        private const int MaximumResponseCharacters = 1024 * 1024;
        private readonly Process process;
        private readonly Task<string> standardError;
        private readonly SemaphoreSlim requestLock = new(1, 1);
        private bool disposed;

        internal WorkerSession(Process process)
        {
            this.process = process;
            this.standardError = process.StandardError.ReadToEndAsync();
        }

        internal async Task<T> RequestAsync<T>(
            string method,
            object payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var response = await this.RequestCoreAsync(method, payload, timeout, cancellationToken)
                .ConfigureAwait(false);
            return response.Payload.Deserialize<T>(JsonOptions)
                ?? throw Failure("package.worker_response_invalid", "Package worker response payload is invalid.");
        }

        internal async Task RequestAsync(
            string method,
            object payload,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            _ = await this.RequestCoreAsync(method, payload, timeout, cancellationToken).ConfigureAwait(false);

        internal async Task StopAsync(CancellationToken cancellationToken)
        {
            if (this.process.HasExited)
            {
                return;
            }
            await this.RequestAsync("stop", new { }, TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
            await this.RequestAsync("shutdown", new { }, TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await this.process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (this.disposed)
            {
                return;
            }
            this.disposed = true;
            try
            {
                if (!this.process.HasExited)
                {
                    this.process.Kill(entireProcessTree: true);
                }
                await this.process.WaitForExitAsync().ConfigureAwait(false);
                _ = await this.standardError.ConfigureAwait(false);
            }
            finally
            {
                this.requestLock.Dispose();
                this.process.Dispose();
            }
        }

        private async Task<PackageWorkerProtocolResponse> RequestCoreAsync(
            string method,
            object payload,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            await this.requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this.process.HasExited)
                {
                    var error = await this.standardError.ConfigureAwait(false);
                    throw Failure(
                        "package.worker_exited",
                        string.IsNullOrWhiteSpace(error)
                            ? "Package worker exited unexpectedly."
                            : "Package worker exited unexpectedly; inspect server logs.");
                }

                var requestId = Guid.NewGuid().ToString("N");
                var request = new PackageWorkerProtocolRequest(
                    requestId,
                    method,
                    JsonSerializer.SerializeToElement(payload, JsonOptions),
                    checked((int)Math.Clamp(timeout.TotalMilliseconds, 1, 300_000)));
                await this.process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions))
                    .ConfigureAwait(false);
                await this.process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(timeout + TimeSpan.FromSeconds(2));
                var line = await this.process.StandardOutput.ReadLineAsync(deadline.Token).ConfigureAwait(false);
                if (line is null || line.Length is 0 or > MaximumResponseCharacters)
                {
                    throw Failure("package.worker_response_invalid", "Package worker response is invalid.");
                }
                var response = JsonSerializer.Deserialize<PackageWorkerProtocolResponse>(line, JsonOptions)
                    ?? throw Failure("package.worker_response_invalid", "Package worker response is invalid.");
                if (!string.Equals(response.Id, requestId, StringComparison.Ordinal))
                {
                    throw Failure("package.worker_response_mismatch", "Package worker response identifier does not match.");
                }
                if (!response.Succeeded)
                {
                    throw Failure(
                        response.ErrorCode ?? "package.worker_operation_failed",
                        response.ErrorDetail ?? "Package worker operation failed.");
                }
                return response;
            }
            finally
            {
                this.requestLock.Release();
            }
        }
    }
}
