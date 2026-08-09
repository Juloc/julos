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

    /// <summary>Installs or updates, configures and enables one official package.</summary>
    Task<PackageInstallationSnapshot> InstallOrUpdateAsync(
        string packageId,
        CancellationToken cancellationToken = default);
}
