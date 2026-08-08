using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using JulOS.Application.Concurrency;
using JulOS.Application.Packages;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

internal interface IPackageWorkerSupervisor
{
    Task<PackageValidationResult> ValidateAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken);

    Task StartAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, string> configuration,
        PackageDatabaseIdentity database,
        CancellationToken cancellationToken);

    Task StopAsync(string packageId, CancellationToken cancellationToken);
}

internal sealed class DisabledPackageWorkerSupervisor : IPackageWorkerSupervisor
{
    public Task<PackageValidationResult> ValidateAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, string> configuration,
        CancellationToken cancellationToken) => Task.FromResult(new PackageValidationResult(true, []));

    public Task StartAsync(
        PackageManifest manifest,
        IReadOnlyDictionary<string, string> configuration,
        PackageDatabaseIdentity database,
        CancellationToken cancellationToken)
    {
        if (manifest.Runtime.Kind != "none")
        {
            throw new PackageManagementException(
                "package.worker_supervisor_unavailable",
                "A package runtime was requested but no worker supervisor is configured.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(string packageId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record InstalledPackageMetadata(
    string PackageId,
    string Version,
    string ArtifactDigest,
    string PublisherId,
    string PublisherKeyId,
    PackageManifest Manifest,
    IReadOnlyDictionary<string, string> Configuration,
    bool ConfigurationRequired,
    bool WorkerHealthy,
    PackageDatabaseIdentity? Database);

/// <summary>Coordinates verified artifacts, isolated storage and worker lifecycle.</summary>
internal sealed class PostgresPackageManagementService : IPackageManagementService, IDisposable
{
    private const long MaximumArtifactBytes = 1024L * 1024 * 1024;
    private readonly CoreDbContext context;
    private readonly PackageArtifactVerifier verifier;
    private readonly PostgresPackageStorageProvisioner storage;
    private readonly IPackageWorkerSupervisor workers;
    private readonly string packageRoot;
    private readonly TimeProvider timeProvider;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);

    public PostgresPackageManagementService(
        CoreDbContext context,
        PackageArtifactVerifier verifier,
        PostgresPackageStorageProvisioner storage,
        IPackageWorkerSupervisor workers,
        string packageRoot,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.verifier = verifier;
        this.storage = storage;
        this.workers = workers;
        this.packageRoot = Path.GetFullPath(packageRoot);
        this.timeProvider = timeProvider;
        Directory.CreateDirectory(this.packageRoot);
    }

    public async Task<IReadOnlyList<PackageInstallationSnapshot>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await this.context.PackageInstallations
            .AsNoTracking()
            .OrderBy(row => row.PackageId)
            .ToArrayAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<PackageInstallationSnapshot>(rows.Length);
        foreach (var row in rows)
        {
            InstalledPackageMetadata? metadata;
            try
            {
                metadata = await ReadMetadataAsync(row.PackageId, cancellationToken).ConfigureAwait(false);
            }
            catch (PackageManagementException)
            {
                // A row without readable metadata (for example an installation that faulted
                // before it finished extracting an artifact) must not hide every other listed
                // package; its own state and fault fields already describe what happened.
                metadata = null;
            }

            result.Add(metadata is null
                ? UnavailableMetadataSnapshot(row)
                : ToSnapshot(row, metadata));
        }
        return result;
    }

    private static PackageInstallationSnapshot UnavailableMetadataSnapshot(PackageInstallationRow row) => new(
        row.Id,
        row.PackageId,
        Version: string.Empty,
        StateName(row.State),
        row.Revision,
        row.FaultCode,
        row.FaultDetail,
        row.FaultedAtUtc,
        ConfigurationRequired: false,
        WorkerHealthy: false,
        ArtifactDigest: string.Empty);

    public async Task<PackageInstallationSnapshot> InstallAsync(
        PackageInstallInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await this.lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var operationPath = OperationPath(input.OperationKey);
            if (File.Exists(operationPath))
            {
                var recordedPackageId = await File.ReadAllTextAsync(operationPath, cancellationToken).ConfigureAwait(false);
                var recorded = await RequireRowAsync(recordedPackageId, cancellationToken).ConfigureAwait(false);
                return ToSnapshot(recorded, await ReadMetadataAsync(recordedPackageId, cancellationToken).ConfigureAwait(false));
            }

            await using var artifact = await BufferArtifactAsync(input.Artifact, cancellationToken).ConfigureAwait(false);
            var verifiedArtifact = VerifyArtifact(artifact, input);
            using var archive = new ZipArchive(artifact, ZipArchiveMode.Read, leaveOpen: true);
            var manifestEntry = archive.GetEntry("manifest.json")
                ?? throw Failure("package.manifest_missing", "Package archive has no manifest.json.");
            byte[] manifestBytes;
            await using (var manifestStream = manifestEntry.Open())
            using (var manifestBuffer = new MemoryStream())
            {
                await manifestStream.CopyToAsync(manifestBuffer, cancellationToken).ConfigureAwait(false);
                manifestBytes = manifestBuffer.ToArray();
            }

            PackageManifest manifest;
            try
            {
                using var manifestStream = new MemoryStream(manifestBytes, writable: false);
                manifest = PackageManifestReader.Read(manifestStream);
            }
            catch (PackageManifestException exception)
            {
                // The reader raises its own exception type so JulOS.PackageSdk stays free of
                // this service's contract; translate it here, the one place that calls it.
                throw Failure(exception.Code, exception.Message, exception);
            }
            if (!string.Equals(manifest.PublisherId, input.PublisherId, StringComparison.Ordinal))
            {
                throw Failure("package.publisher_mismatch", "Manifest publisher does not match the verified publisher.");
            }

            var existing = await this.context.PackageInstallations
                .SingleOrDefaultAsync(row => row.PackageId == manifest.PackageId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                throw Failure("package.already_installed", "The package is already installed.");
            }

            var now = this.timeProvider.GetUtcNow();
            var row = new PackageInstallationRow
            {
                Id = Guid.CreateVersion7(now),
                PackageId = manifest.PackageId,
                State = PackageInstallationState.Installing,
                Revision = 1,
            };
            this.context.PackageInstallations.Add(row);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var versionRoot = VersionRoot(manifest.PackageId, manifest.Version);
                if (Directory.Exists(versionRoot))
                {
                    Directory.Delete(versionRoot, recursive: true);
                }
                Directory.CreateDirectory(versionRoot);
                await ExtractArchiveAsync(archive, versionRoot, cancellationToken).ConfigureAwait(false);
                var database = await this.storage.ProvisionAsync(manifest.PackageId, cancellationToken).ConfigureAwait(false);
                var metadata = new InstalledPackageMetadata(
                    manifest.PackageId,
                    manifest.Version,
                    verifiedArtifact.DigestSha256,
                    input.PublisherId,
                    input.PublisherKeyId,
                    manifest,
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    ConfigurationRequired: true,
                    WorkerHealthy: false,
                    database);
                await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
                await PackageApplicationRegistration.SynchronizeAsync(
                    this.context,
                    manifest,
                    enabled: false,
                    this.timeProvider,
                    cancellationToken).ConfigureAwait(false);
                row.State = PackageInstallationState.Installed;
                row.Revision = checked(row.Revision + 1);
                await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(Path.GetDirectoryName(operationPath)!);
                await File.WriteAllTextAsync(operationPath, manifest.PackageId, cancellationToken).ConfigureAwait(false);
                return ToSnapshot(row, metadata);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                row.State = PackageInstallationState.Faulted;
                row.FaultCode = "package.install_failed";
                row.FaultDetail = SafeDetail(exception);
                row.FaultedAtUtc = this.timeProvider.GetUtcNow();
                row.Revision = checked(row.Revision + 1);
                await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                throw Failure("package.install_failed", "Package installation failed.", exception);
            }
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }

    public async Task<PackageInstallationSnapshot> ConfigureAsync(
        string packageId,
        PackageConfigurationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await this.lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var row = await RequireRowAsync(packageId, cancellationToken).ConfigureAwait(false);
            EnsureRevision(row, input.Revision);
            if (row.State is not (PackageInstallationState.Installed or PackageInstallationState.Disabled))
            {
                throw Failure("package.configuration_state_invalid", "Package cannot be configured in its current state.");
            }
            var metadata = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
            var validation = await this.workers.ValidateAsync(metadata.Manifest, input.Values, cancellationToken)
                .ConfigureAwait(false);
            if (!validation.Valid || validation.Issues.Any(issue => issue.Blocking))
            {
                throw new PackageManagementException(
                    "package.configuration_invalid",
                    string.Join("; ", validation.Issues.Where(issue => issue.Blocking).Select(issue => issue.Message)));
            }

            row.State = PackageInstallationState.Configuring;
            row.Revision = checked(row.Revision + 1);
            metadata = metadata with
            {
                Configuration = new Dictionary<string, string>(input.Values, StringComparer.Ordinal),
                ConfigurationRequired = false,
                WorkerHealthy = false,
            };
            await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
            row.State = PackageInstallationState.Disabled;
            row.Revision = checked(row.Revision + 1);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ToSnapshot(row, metadata);
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }

    public async Task<PackageInstallationSnapshot> EnableAsync(
        string packageId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        await this.lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var row = await RequireRowAsync(packageId, cancellationToken).ConfigureAwait(false);
            EnsureRevision(row, revision);
            var metadata = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
            if (row.State != PackageInstallationState.Disabled || metadata.ConfigurationRequired || metadata.Database is null)
            {
                throw Failure("package.enable_state_invalid", "Package is not ready to enable.");
            }

            row.State = PackageInstallationState.Starting;
            row.Revision = checked(row.Revision + 1);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.workers.StartAsync(
                    metadata.Manifest,
                    metadata.Configuration,
                    metadata.Database,
                    cancellationToken).ConfigureAwait(false);
                metadata = metadata with { WorkerHealthy = true };
                await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
                await PackageApplicationRegistration.SynchronizeAsync(
                    this.context,
                    metadata.Manifest,
                    enabled: true,
                    this.timeProvider,
                    cancellationToken).ConfigureAwait(false);
                row.State = PackageInstallationState.Enabled;
                row.Revision = checked(row.Revision + 1);
                await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ToSnapshot(row, metadata);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await FaultAsync(row, metadata, "package.worker_start_failed", exception, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }

    public async Task<PackageInstallationSnapshot> DisableAsync(
        string packageId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        await this.lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var row = await RequireRowAsync(packageId, cancellationToken).ConfigureAwait(false);
            EnsureRevision(row, revision);
            var metadata = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
            if (row.State != PackageInstallationState.Enabled)
            {
                throw Failure("package.disable_state_invalid", "Package is not enabled.");
            }

            row.State = PackageInstallationState.Stopping;
            row.Revision = checked(row.Revision + 1);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.workers.StopAsync(packageId, cancellationToken).ConfigureAwait(false);
                metadata = metadata with { WorkerHealthy = false };
                await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
                await PackageApplicationRegistration.SynchronizeAsync(
                    this.context,
                    metadata.Manifest,
                    enabled: false,
                    this.timeProvider,
                    cancellationToken).ConfigureAwait(false);
                row.State = PackageInstallationState.Disabled;
                row.Revision = checked(row.Revision + 1);
                await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ToSnapshot(row, metadata);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return await FaultAsync(row, metadata, "package.worker_stop_failed", exception, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }

    public async Task<PackageInstallationSnapshot> RemoveAsync(
        string packageId,
        PackageRemovalInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await this.lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var row = await RequireRowAsync(packageId, cancellationToken).ConfigureAwait(false);
            EnsureRevision(row, input.Revision);
            InstalledPackageMetadata? metadata;
            try
            {
                metadata = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
            }
            catch (PackageManagementException)
            {
                // An installation that faulted before it finished extracting an artifact
                // never registered an application or widget, so there is nothing to
                // synchronize below; only its row and any partial files need to go.
                metadata = null;
            }

            if (row.State == PackageInstallationState.Enabled)
            {
                await this.workers.StopAsync(packageId, cancellationToken).ConfigureAwait(false);
            }
            if (row.State is PackageInstallationState.Installing
                or PackageInstallationState.Starting
                or PackageInstallationState.Stopping
                or PackageInstallationState.Updating
                or PackageInstallationState.Removing)
            {
                throw Failure("package.remove_state_invalid", "Package is busy and cannot be removed.");
            }

            row.State = PackageInstallationState.Removing;
            row.FaultCode = null;
            row.FaultDetail = null;
            row.FaultedAtUtc = null;
            row.Revision = checked(row.Revision + 1);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await this.storage.DropAsync(packageId, input.DeletePackageData, cancellationToken).ConfigureAwait(false);
            var packagePath = PackageRoot(packageId);
            if (Directory.Exists(packagePath))
            {
                Directory.Delete(packagePath, recursive: true);
            }
            if (metadata is not null)
            {
                await PackageApplicationRegistration.SynchronizeAsync(
                    this.context,
                    metadata.Manifest,
                    enabled: false,
                    this.timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }

            this.context.PackageInstallations.Remove(row);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new PackageInstallationSnapshot(
                row.Id,
                packageId,
                metadata?.Version ?? string.Empty,
                "removed",
                row.Revision,
                null,
                null,
                null,
                false,
                false,
                metadata?.ArtifactDigest ?? string.Empty);
        }
        finally
        {
            this.lifecycleLock.Release();
        }
    }

    private async Task<PackageInstallationSnapshot> FaultAsync(
        PackageInstallationRow row,
        InstalledPackageMetadata metadata,
        string code,
        Exception exception,
        CancellationToken cancellationToken)
    {
        metadata = metadata with { WorkerHealthy = false };
        await WriteMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
        await PackageApplicationRegistration.SynchronizeAsync(
            this.context,
            metadata.Manifest,
            enabled: false,
            this.timeProvider,
            cancellationToken).ConfigureAwait(false);
        row.State = PackageInstallationState.Faulted;
        row.FaultCode = code;
        row.FaultDetail = SafeDetail(exception);
        row.FaultedAtUtc = this.timeProvider.GetUtcNow();
        row.Revision = checked(row.Revision + 1);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToSnapshot(row, metadata);
    }

    private async Task<PackageInstallationRow> RequireRowAsync(
        string packageId,
        CancellationToken cancellationToken) =>
        await this.context.PackageInstallations.SingleOrDefaultAsync(
            row => row.PackageId == packageId,
            cancellationToken).ConfigureAwait(false)
        ?? throw Failure("package.not_found", "Package is not installed.");

    private static void EnsureRevision(PackageInstallationRow row, int revision)
    {
        if (revision < 1 || row.Revision != revision)
        {
            throw new ConcurrencyConflictException(
                row.Revision,
                new InvalidOperationException("The package installation changed concurrently."));
        }
    }

    private static PackageInstallationSnapshot ToSnapshot(
        PackageInstallationRow row,
        InstalledPackageMetadata metadata) => new(
        row.Id,
        row.PackageId,
        metadata.Version,
        StateName(row.State),
        row.Revision,
        row.FaultCode,
        row.FaultDetail,
        row.FaultedAtUtc,
        metadata.ConfigurationRequired,
        metadata.WorkerHealthy,
        metadata.ArtifactDigest);

    private async Task<InstalledPackageMetadata> ReadMetadataAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        var path = MetadataPath(packageId);
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // An installation that faulted before it finished extracting an artifact never
            // writes this file. The row itself already carries the fault the caller needs.
            throw Failure("package.metadata_unavailable", "Package metadata is not available.");
        }

        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<InstalledPackageMetadata>(stream, this.jsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw Failure("package.metadata_invalid", "Package metadata is invalid.");
        }
    }

    private async Task WriteMetadataAsync(
        InstalledPackageMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = MetadataPath(metadata.PackageId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, this.jsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private VerifiedPackageArtifact VerifyArtifact(MemoryStream artifact, PackageInstallInput input)
    {
        if (!artifact.TryGetBuffer(out var buffer))
        {
            throw Failure("package.artifact_buffer_invalid", "Package archive could not be verified.");
        }

        var artifactBytes = buffer.AsSpan(0, checked((int)artifact.Length));
        var observedDigest = Convert.ToHexStringLower(SHA256.HashData(artifactBytes));
        var expectedDigest = string.IsNullOrWhiteSpace(input.ExpectedDigest)
            ? observedDigest
            : input.ExpectedDigest;
        try
        {
            return this.verifier.Verify(
                artifactBytes,
                input.Signature,
                expectedDigest,
                input.PublisherId,
                input.PublisherKeyId);
        }
        catch (PackageArtifactVerificationException exception)
        {
            // The verifier raises its own exception type so it stays free of the package
            // lifecycle contract; every caller in this service otherwise only throws or
            // catches PackageManagementException, so translate it at this one boundary.
            throw Failure(exception.Code, exception.Message, exception);
        }
    }

    private static async Task<MemoryStream> BufferArtifactAsync(Stream source, CancellationToken cancellationToken)
    {
        var target = new MemoryStream();
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumArtifactBytes)
            {
                target.Dispose();
                throw Failure("package.artifact_too_large", "Package artifact exceeds the maximum size.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        target.Position = 0;
        return target;
    }

    private static async Task ExtractArchiveAsync(
        ZipArchive archive,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destinationRoot);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isDirectoryEntry = entry.FullName.EndsWith('/');
            var pathToValidate = isDirectoryEntry ? entry.FullName[..^1] : entry.FullName;
            if (string.IsNullOrWhiteSpace(entry.FullName)
                || Path.IsPathRooted(entry.FullName)
                || entry.FullName.Contains('\\')
                || pathToValidate.Length == 0
                || pathToValidate.Split('/').Any(segment => segment is ".." or "." or ""))
            {
                throw Failure("package.archive_path_invalid", "Package archive contains an invalid path.");
            }
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(destination, root, StringComparison.Ordinal))
            {
                throw Failure("package.archive_path_escape", "Package archive path escapes its package root.");
            }
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private string PackageRoot(string packageId) => Path.Combine(this.packageRoot, packageId);

    private string VersionRoot(string packageId, string version) => Path.Combine(PackageRoot(packageId), "versions", version);

    private string MetadataPath(string packageId) => Path.Combine(PackageRoot(packageId), "state.json");

    private string OperationPath(string operationKey)
    {
        if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 256 || operationKey.Any(char.IsControl))
        {
            throw Failure("package.operation_key_invalid", "Package operation key is invalid.");
        }
        var safe = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operationKey)));
        return Path.Combine(this.packageRoot, ".operations", safe + ".txt");
    }

    private static string SafeDetail(Exception exception)
    {
        var value = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= 1024 ? value : value[..1024];
    }

    private static string StateName(PackageInstallationState state) => state.ToString().ToLowerInvariant();

    private static PackageManagementException Failure(string code, string message, Exception? inner = null) =>
        new(code, message, inner);

    public void Dispose()
    {
        this.lifecycleLock.Dispose();
    }
}
