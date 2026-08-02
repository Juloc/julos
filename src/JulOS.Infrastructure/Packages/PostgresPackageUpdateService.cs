using System.IO.Compression;
using System.Text.Json;

using JulOS.Application.Concurrency;
using JulOS.Application.Packages;
using JulOS.Domain.Packages;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>Validates updates before changing package state and preserves one rollback version.</summary>
internal sealed class PostgresPackageUpdateService : IPackageUpdateService
{
    private const long MaximumArtifactBytes = 1024L * 1024 * 1024;
    private readonly CoreDbContext context;
    private readonly PackageArtifactVerifier verifier;
    private readonly IPackageWorkerSupervisor workers;
    private readonly string packageRoot;
    private readonly TimeProvider timeProvider;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim updateLock = new(1, 1);

    public PostgresPackageUpdateService(
        CoreDbContext context,
        PackageArtifactVerifier verifier,
        IPackageWorkerSupervisor workers,
        string packageRoot,
        TimeProvider timeProvider)
    {
        this.context = context;
        this.verifier = verifier;
        this.workers = workers;
        this.packageRoot = Path.GetFullPath(packageRoot);
        this.timeProvider = timeProvider;
    }

    public async Task<PackageUpdatePreview> PreviewAsync(
        string packageId,
        PackageUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = await RequireRowAsync(packageId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(row, input.Revision);
        var current = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
        var candidate = await ReadCandidateAsync(input, cancellationToken).ConfigureAwait(false);
        EnsureIdentity(current, candidate.Manifest, packageId);
        return Preview(current, candidate.Manifest);
    }

    public async Task<PackageInstallationSnapshot> UpdateAsync(
        string packageId,
        PackageUpdateInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await this.updateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var row = await RequireRowAsync(packageId, cancellationToken).ConfigureAwait(false);
            EnsureRevision(row, input.Revision);
            if (row.State is not (
                PackageInstallationState.Installed
                or PackageInstallationState.Disabled
                or PackageInstallationState.Enabled
                or PackageInstallationState.Faulted))
            {
                throw Failure("package.update_state_invalid", "Package cannot be updated in its current state.");
            }

            var current = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
            var candidate = await ReadCandidateAsync(input, cancellationToken).ConfigureAwait(false);
            EnsureIdentity(current, candidate.Manifest, packageId);
            var preview = Preview(current, candidate.Manifest);
            if (preview.RequiresExplicitApproval && !input.AllowIrreversibleMigrations)
            {
                throw Failure(
                    "package.update_irreversible_approval_required",
                    "The update contains irreversible migrations and requires explicit approval.");
            }

            var previousState = row.State;
            var wasEnabled = previousState == PackageInstallationState.Enabled;
            if (wasEnabled)
            {
                await this.workers.StopAsync(packageId, cancellationToken).ConfigureAwait(false);
            }

            row.State = PackageInstallationState.Updating;
            row.FaultCode = null;
            row.FaultDetail = null;
            row.FaultedAtUtc = null;
            row.Revision = checked(row.Revision + 1);
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var targetRoot = VersionRoot(packageId, candidate.Manifest.Version);
            var stagingRoot = targetRoot + ".staging-" + Guid.NewGuid().ToString("N");
            try
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
                Directory.CreateDirectory(stagingRoot);
                await ExtractAsync(candidate.Archive, stagingRoot, cancellationToken).ConfigureAwait(false);
                await WriteMigrationPlanAsync(
                    stagingRoot,
                    candidate.Manifest.Migrations,
                    cancellationToken).ConfigureAwait(false);
                if (Directory.Exists(targetRoot))
                {
                    Directory.Delete(targetRoot, recursive: true);
                }
                Directory.Move(stagingRoot, targetRoot);

                var updated = current with
                {
                    Version = candidate.Manifest.Version,
                    ArtifactDigest = input.ExpectedDigest,
                    PublisherId = input.PublisherId,
                    PublisherKeyId = input.PublisherKeyId,
                    Manifest = candidate.Manifest,
                    WorkerHealthy = false,
                };
                await WriteMetadataAsync(updated, cancellationToken).ConfigureAwait(false);

                row.State = PackageInstallationState.Installed;
                row.Revision = checked(row.Revision + 1);
                if (!updated.ConfigurationRequired)
                {
                    row.State = PackageInstallationState.Configuring;
                    row.Revision = checked(row.Revision + 1);
                    row.State = PackageInstallationState.Disabled;
                    row.Revision = checked(row.Revision + 1);
                }
                await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                if (wasEnabled && !updated.ConfigurationRequired && updated.Database is not null)
                {
                    row.State = PackageInstallationState.Starting;
                    row.Revision = checked(row.Revision + 1);
                    await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await this.workers.StartAsync(
                        updated.Manifest,
                        updated.Configuration,
                        updated.Database,
                        cancellationToken).ConfigureAwait(false);
                    updated = updated with { WorkerHealthy = true };
                    await WriteMetadataAsync(updated, cancellationToken).ConfigureAwait(false);
                    row.State = PackageInstallationState.Enabled;
                    row.Revision = checked(row.Revision + 1);
                    await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                RetainCurrentAndPreviousVersion(packageId, updated.Version, current.Version);
                return Snapshot(row, updated);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (Directory.Exists(stagingRoot))
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }

                if (preview.IrreversibleMigrations.Count == 0)
                {
                    await WriteMetadataAsync(current, cancellationToken).ConfigureAwait(false);
                    row.State = previousState == PackageInstallationState.Faulted
                        ? PackageInstallationState.Installed
                        : previousState;
                    row.FaultCode = null;
                    row.FaultDetail = null;
                    row.FaultedAtUtc = null;
                    row.Revision = checked(row.Revision + 1);
                    await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    if (wasEnabled && current.Database is not null)
                    {
                        await this.workers.StartAsync(
                            current.Manifest,
                            current.Configuration,
                            current.Database,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    row.State = PackageInstallationState.Faulted;
                    row.FaultCode = "package.update_failed_after_irreversible_migration";
                    row.FaultDetail = SafeDetail(exception);
                    row.FaultedAtUtc = this.timeProvider.GetUtcNow();
                    row.Revision = checked(row.Revision + 1);
                    await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                throw Failure("package.update_failed", "Package update failed.", exception);
            }
        }
        finally
        {
            this.updateLock.Release();
        }
    }

    private async Task<CandidateArtifact> ReadCandidateAsync(
        PackageUpdateInput input,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var bytes = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.Artifact.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumArtifactBytes)
            {
                buffer.Dispose();
                throw Failure("package.artifact_too_large", "Package artifact exceeds the maximum size.");
            }
            await buffer.WriteAsync(bytes.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        buffer.Position = 0;
        var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw Failure("package.manifest_missing", "Package archive has no manifest.json.");
        byte[] manifestBytes;
        await using (var stream = manifestEntry.Open())
        using (var manifestBuffer = new MemoryStream())
        {
            await stream.CopyToAsync(manifestBuffer, cancellationToken).ConfigureAwait(false);
            manifestBytes = manifestBuffer.ToArray();
        }
        _ = this.verifier.Verify(
            manifestBytes,
            input.Signature,
            input.ExpectedDigest,
            input.PublisherId,
            input.PublisherKeyId);
        PackageManifest manifest;
        using (var stream = new MemoryStream(manifestBytes, writable: false))
        {
            manifest = PackageManifestReader.Read(stream);
        }
        return new CandidateArtifact(buffer, archive, manifest);
    }

    private static PackageUpdatePreview Preview(
        InstalledPackageMetadata current,
        PackageManifest candidate)
    {
        var oldMigrations = current.Manifest.Migrations
            .ToDictionary(migration => migration.MigrationId, StringComparer.Ordinal);
        var newMigrations = candidate.Migrations
            .Where(migration => !oldMigrations.TryGetValue(migration.MigrationId, out var old)
                || !string.Equals(old.Digest, migration.Digest, StringComparison.Ordinal))
            .ToArray();
        var irreversible = newMigrations
            .Where(migration => !migration.Reversible)
            .Select(migration => migration.MigrationId)
            .ToArray();
        return new PackageUpdatePreview(
            current.PackageId,
            current.Version,
            candidate.Version,
            newMigrations.Select(migration => migration.MigrationId).ToArray(),
            irreversible,
            irreversible.Length > 0);
    }

    private static void EnsureIdentity(
        InstalledPackageMetadata current,
        PackageManifest candidate,
        string packageId)
    {
        if (!string.Equals(candidate.PackageId, packageId, StringComparison.Ordinal)
            || !string.Equals(candidate.PublisherId, current.PublisherId, StringComparison.Ordinal)
            || string.Equals(candidate.Version, current.Version, StringComparison.Ordinal))
        {
            throw Failure("package.update_identity_invalid", "Package update identity or version is invalid.");
        }
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

    private async Task<InstalledPackageMetadata> ReadMetadataAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            MetadataPath(packageId),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            true);
        return await JsonSerializer.DeserializeAsync<InstalledPackageMetadata>(
            stream,
            this.jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw Failure("package.metadata_invalid", "Package metadata is invalid.");
    }

    private async Task WriteMetadataAsync(
        InstalledPackageMetadata metadata,
        CancellationToken cancellationToken)
    {
        var path = MetadataPath(metadata.PackageId);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                metadata,
                this.jsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task ExtractAsync(
        ZipArchive archive,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destinationRoot);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.FullName)
                || Path.IsPathRooted(entry.FullName)
                || entry.FullName.Contains('\\')
                || entry.FullName.Split('/').Any(segment => segment is ".." or "." or ""))
            {
                throw Failure("package.archive_path_invalid", "Package archive path is invalid.");
            }
            var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw Failure("package.archive_path_escape", "Package archive path escapes its root.");
            }
            if (entry.FullName.EndsWith('/', StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = entry.Open();
            await using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                true);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task WriteMigrationPlanAsync(
        string root,
        IReadOnlyList<PackageMigrationManifest> migrations,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            Path.Combine(root, "applied-migrations.json"),
            JsonSerializer.Serialize(migrations),
            cancellationToken);

    private void RetainCurrentAndPreviousVersion(
        string packageId,
        string currentVersion,
        string previousVersion)
    {
        var versionsRoot = Path.Combine(this.packageRoot, packageId, "versions");
        if (!Directory.Exists(versionsRoot))
        {
            return;
        }
        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
        {
            var name = Path.GetFileName(directory);
            if (!string.Equals(name, currentVersion, StringComparison.Ordinal)
                && !string.Equals(name, previousVersion, StringComparison.Ordinal))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private PackageInstallationSnapshot Snapshot(
        PackageInstallationRow row,
        InstalledPackageMetadata metadata) => new(
        row.Id,
        row.PackageId,
        metadata.Version,
        row.State.ToString().ToLowerInvariant(),
        row.Revision,
        row.FaultCode,
        row.FaultDetail,
        row.FaultedAtUtc,
        metadata.ConfigurationRequired,
        metadata.WorkerHealthy,
        metadata.ArtifactDigest);

    private string VersionRoot(string packageId, string version) =>
        Path.Combine(this.packageRoot, packageId, "versions", version);

    private string MetadataPath(string packageId) =>
        Path.Combine(this.packageRoot, packageId, "state.json");

    private static string SafeDetail(Exception exception)
    {
        var detail = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return detail.Length <= 1024 ? detail : detail[..1024];
    }

    private static PackageManagementException Failure(
        string code,
        string message,
        Exception? inner = null) => new(code, message, inner);

    private sealed record CandidateArtifact(
        MemoryStream Buffer,
        ZipArchive Archive,
        PackageManifest Manifest) : IDisposable
    {
        public void Dispose()
        {
            this.Archive.Dispose();
            this.Buffer.Dispose();
        }
    }
}
