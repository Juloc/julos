using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using JulOS.Application.Packages;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Packages;

internal sealed class OfficialPackageCatalogIndex
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex DigestPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, OfficialPackageCatalogItem> items;

    private OfficialPackageCatalogIndex(
        IReadOnlyList<OfficialPackageCatalogItem> items,
        TrustedPackagePublisher? trustedPublisher)
    {
        this.items = items.ToDictionary(item => item.Entry.PackageId, StringComparer.Ordinal);
        this.TrustedPublisher = trustedPublisher;
    }

    public TrustedPackagePublisher? TrustedPublisher { get; }

    public IReadOnlyList<OfficialPackageCatalogItem> Items => this.items.Values
        .OrderBy(item => item.Entry.DisplayNameEn, StringComparer.Ordinal)
        .ToArray();

    public OfficialPackageCatalogItem Require(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return this.items.TryGetValue(packageId, out var item)
            ? item
            : throw new PackageManagementException("package.catalog_not_found", "The official package is not available in this JulOS release.");
    }

    public static OfficialPackageCatalogIndex Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        var catalogPath = Path.Combine(fullRoot, "catalog.json");
        if (!File.Exists(catalogPath))
        {
            return new OfficialPackageCatalogIndex([], null);
        }

        OfficialPackageCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<OfficialPackageCatalogDocument>(File.ReadAllText(catalogPath), JsonOptions)
                ?? throw new InvalidDataException("Official package catalog is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Official package catalog is invalid.", exception);
        }

        if (!string.Equals(document.SchemaVersion, "1", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.PublisherId)
            || string.IsNullOrWhiteSpace(document.KeyId))
        {
            throw new InvalidOperationException("Official package catalog metadata is invalid.");
        }

        var publicKeyPath = SafeFile(fullRoot, document.PublicKeyFile);
        var publisher = new TrustedPackagePublisher(
            document.PublisherId,
            document.KeyId,
            File.ReadAllText(publicKeyPath));
        var items = new List<OfficialPackageCatalogItem>(document.Packages.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in document.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageId)
                || string.IsNullOrWhiteSpace(package.Version)
                || string.IsNullOrWhiteSpace(package.DisplayNameEn)
                || string.IsNullOrWhiteSpace(package.DisplayNameDe)
                || !DigestPattern.IsMatch(package.Sha256)
                || !ids.Add(package.PackageId))
            {
                throw new InvalidOperationException("Official package catalog entry is invalid.");
            }

            items.Add(new OfficialPackageCatalogItem(
                new OfficialPackageCatalogEntry(
                    package.PackageId,
                    package.Version,
                    package.DisplayNameEn,
                    package.DisplayNameDe,
                    package.DescriptionEn ?? string.Empty,
                    package.DescriptionDe ?? string.Empty,
                    document.PublisherId,
                    document.KeyId,
                    package.Sha256,
                    new Dictionary<string, string>(package.DefaultConfiguration, StringComparer.Ordinal)),
                SafeFile(fullRoot, package.ArtifactFile),
                SafeFile(fullRoot, package.SignatureFile)));
        }

        return new OfficialPackageCatalogIndex(items, publisher);
    }

    private static string SafeFile(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Official package catalog contains an invalid file path.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.Ordinal) || !File.Exists(fullPath))
        {
            throw new InvalidOperationException("Official package catalog file is missing or outside its root.");
        }
        return fullPath;
    }

    private sealed record OfficialPackageCatalogDocument(
        string SchemaVersion,
        string PublisherId,
        string KeyId,
        string PublicKeyFile,
        IReadOnlyList<OfficialPackageCatalogDocumentEntry> Packages);

    private sealed record OfficialPackageCatalogDocumentEntry(
        string PackageId,
        string Version,
        string DisplayNameEn,
        string DisplayNameDe,
        string? DescriptionEn,
        string? DescriptionDe,
        string ArtifactFile,
        string SignatureFile,
        string Sha256,
        IReadOnlyDictionary<string, string> DefaultConfiguration);
}

internal sealed record OfficialPackageCatalogItem(
    OfficialPackageCatalogEntry Entry,
    string ArtifactPath,
    string SignaturePath);

internal sealed class OfficialPackageStoreService : IOfficialPackageStoreService
{
    private readonly OfficialPackageCatalogIndex catalog;
    private readonly IPackageManagementService packages;
    private readonly IPackageUpdateService updates;

    public OfficialPackageStoreService(
        OfficialPackageCatalogIndex catalog,
        IPackageManagementService packages,
        IPackageUpdateService updates)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.packages = packages ?? throw new ArgumentNullException(nameof(packages));
        this.updates = updates ?? throw new ArgumentNullException(nameof(updates));
    }

    public async Task<IReadOnlyList<OfficialPackageStoreEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var installations = (await this.packages.ListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(package => package.PackageId, StringComparer.Ordinal);
        return this.catalog.Items
            .Select(item => new OfficialPackageStoreEntry(
                item.Entry,
                installations.GetValueOrDefault(item.Entry.PackageId)))
            .ToArray();
    }

    public async Task<PackageInstallationSnapshot> InstallOrUpdateAsync(
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var item = this.catalog.Require(packageId);
        var existing = (await this.packages.ListAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(package => string.Equals(package.PackageId, packageId, StringComparison.Ordinal));

        PackageInstallationSnapshot current;
        if (existing is null)
        {
            await using var artifact = File.OpenRead(item.ArtifactPath);
            var signature = await File.ReadAllBytesAsync(item.SignaturePath, cancellationToken).ConfigureAwait(false);
            current = await this.packages.InstallAsync(
                new PackageInstallInput(
                    artifact,
                    signature,
                    item.Entry.ArtifactDigest,
                    item.Entry.PublisherId,
                    item.Entry.PublisherKeyId,
                    OperationKey(item.Entry)),
                cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(existing.Version, item.Entry.Version, StringComparison.Ordinal))
        {
            await using var artifact = File.OpenRead(item.ArtifactPath);
            var signature = await File.ReadAllBytesAsync(item.SignaturePath, cancellationToken).ConfigureAwait(false);
            current = await this.updates.UpdateAsync(
                packageId,
                new PackageUpdateInput(
                    artifact,
                    signature,
                    item.Entry.ArtifactDigest,
                    item.Entry.PublisherId,
                    item.Entry.PublisherKeyId,
                    existing.Revision,
                    AllowIrreversibleMigrations: false),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            current = existing;
        }

        if (current.ConfigurationRequired)
        {
            current = await this.packages.ConfigureAsync(
                packageId,
                new PackageConfigurationInput(item.Entry.DefaultConfiguration, current.Revision),
                cancellationToken).ConfigureAwait(false);
        }
        if (string.Equals(current.State, "disabled", StringComparison.Ordinal))
        {
            current = await this.packages.EnableAsync(packageId, current.Revision, cancellationToken).ConfigureAwait(false);
        }
        return current;
    }

    private static string OperationKey(OfficialPackageCatalogEntry entry)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{entry.PackageId}\n{entry.Version}"));
        return $"official-{Convert.ToHexStringLower(digest)[..32]}";
    }
}
