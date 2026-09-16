namespace JulOS.Application.Packages;

/// <summary>One official package shipped with the current JulOS release.</summary>
public sealed record OfficialPackageCatalogEntry(
    string PackageId,
    string Version,
    string DisplayNameEn,
    string DisplayNameDe,
    string DescriptionEn,
    string DescriptionDe,
    string PublisherId,
    string PublisherKeyId,
    string ArtifactDigest,
    IReadOnlyDictionary<string, string> DefaultConfiguration);

/// <summary>Catalog entry joined with the current installation state.</summary>
public sealed record OfficialPackageStoreEntry(
    OfficialPackageCatalogEntry Package,
    PackageInstallationSnapshot? Installation);

/// <summary>Official package catalog and one-click lifecycle boundary.</summary>
public interface IOfficialPackageStoreService
{
    /// <summary>Lists every official package together with its installation state.</summary>
    Task<IReadOnlyList<OfficialPackageStoreEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports what installing one official package would mean, changing nothing.</summary>
    /// <remarks>
    /// An official package is signed by a key this installation trusts, and it still
    /// declares rights. Signing does not make those rights safe, so the same confirmation
    /// applies here as to an uploaded artifact — the difference is only where the bytes
    /// come from.
    /// </remarks>
    Task<PackageInstallPreview> PreviewAsync(
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>Installs or updates, configures and enables one official package.</summary>
    /// <param name="packageId">Official package identity.</param>
    /// <param name="acknowledgementDigest">The digest the preview produced, when one is required.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task<PackageInstallationSnapshot> InstallOrUpdateAsync(
        string packageId,
        string? acknowledgementDigest = null,
        CancellationToken cancellationToken = default);
}
