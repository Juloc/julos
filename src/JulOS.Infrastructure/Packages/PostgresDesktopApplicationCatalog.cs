using System.Security.Cryptography;
using System.Text.Json;

using JulOS.Application.Packages;
using JulOS.Domain.Applications;
using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;
using JulOS.PackageSdk;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Packages;

/// <summary>Reads enabled desktop surfaces and verified frontend modules from installed package state.</summary>
internal sealed class PostgresDesktopApplicationCatalog : IDesktopApplicationCatalog
{
    private const int MaximumFrontendBytes = 16 * 1024 * 1024;
    private readonly CoreDbContext context;
    private readonly string packageRoot;
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    public PostgresDesktopApplicationCatalog(CoreDbContext context, string packageRoot)
    {
        this.context = context;
        this.packageRoot = Path.GetFullPath(packageRoot);
    }

    public async Task<IReadOnlyList<DesktopPackageApplication>> ListAsync(
        string viewport,
        CancellationToken cancellationToken = default)
    {
        var viewportClass = ParseViewport(viewport);
        var enabledPackages = await EnabledPackageIdsAsync(cancellationToken).ConfigureAwait(false);
        if (enabledPackages.Length == 0)
        {
            return [];
        }

        var rows = await this.context.ApplicationDefinitions
            .AsNoTracking()
            .Include(row => row.SupportedViewports)
            .Where(row => row.IsEnabled
                && enabledPackages.Contains(row.OwningPackageId)
                && row.SupportedViewports.Any(item => item.ViewportClass == viewportClass))
            .OrderBy(row => row.OwningPackageId)
            .ThenBy(row => row.StableKey)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var metadata = new Dictionary<string, InstalledPackageMetadata>(StringComparer.Ordinal);
        var result = new List<DesktopPackageApplication>(rows.Length);
        foreach (var row in rows)
        {
            if (!metadata.TryGetValue(row.OwningPackageId, out var installed))
            {
                installed = await ReadMetadataAsync(row.OwningPackageId, cancellationToken).ConfigureAwait(false);
                PackageManifestReader.Validate(installed.Manifest);
                metadata.Add(row.OwningPackageId, installed);
            }

            var application = installed.Manifest.Applications.SingleOrDefault(
                item => string.Equals(item.StableKey, row.StableKey, StringComparison.Ordinal));
            var frontend = installed.Manifest.Frontend;
            if (application is null || frontend is null)
            {
                continue;
            }

            result.Add(new DesktopPackageApplication(
                row.Id,
                row.OwningPackageId,
                installed.Version,
                row.StableKey,
                row.DisplayNameKey,
                PolicyName(row.InstancePolicy),
                row.DefaultWidth,
                row.DefaultHeight,
                row.MinimumWidth,
                row.MinimumHeight,
                row.SupportedViewports.Select(item => ViewportName(item.ViewportClass)).Order().ToArray(),
                application.ElementName,
                frontend.Sha256,
                frontend.ExportedElements.ToArray()));
        }

        return result;
    }

    public async Task<IReadOnlyList<DesktopPackageWidget>> ListWidgetsAsync(
        CancellationToken cancellationToken = default)
    {
        var enabledPackages = await EnabledPackageIdsAsync(cancellationToken).ConfigureAwait(false);
        if (enabledPackages.Length == 0)
        {
            return [];
        }

        var result = new List<DesktopPackageWidget>();
        foreach (var packageId in enabledPackages.Order(StringComparer.Ordinal))
        {
            var installed = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
            PackageManifestReader.Validate(installed.Manifest);
            var frontend = installed.Manifest.Frontend;
            if (frontend is null)
            {
                continue;
            }

            foreach (var widget in installed.Manifest.Widgets.OrderBy(item => item.StableKey, StringComparer.Ordinal))
            {
                result.Add(new DesktopPackageWidget(
                    WidgetKey(packageId, widget.StableKey),
                    packageId,
                    installed.Version,
                    widget.StableKey,
                    widget.DisplayNameKey,
                    widget.ElementName,
                    widget.Sizes.ToArray(),
                    widget.DefaultSize,
                    frontend.Sha256,
                    frontend.ExportedElements.ToArray()));
            }
        }

        return result;
    }

    public async Task<DesktopPackageFrontend> ReadFrontendAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var enabled = await this.context.PackageInstallations
            .AsNoTracking()
            .AnyAsync(
                row => row.PackageId == packageId && row.State == PackageInstallationState.Enabled,
                cancellationToken)
            .ConfigureAwait(false);
        if (!enabled)
        {
            throw Failure("package.not_found", "Package is not enabled.");
        }

        var metadata = await ReadMetadataAsync(packageId, cancellationToken).ConfigureAwait(false);
        PackageManifestReader.Validate(metadata.Manifest);
        if (!string.Equals(metadata.PackageId, packageId, StringComparison.Ordinal)
            || !string.Equals(metadata.Version, version, StringComparison.Ordinal)
            || !string.Equals(metadata.Manifest.PackageId, packageId, StringComparison.Ordinal)
            || !string.Equals(metadata.Manifest.Version, version, StringComparison.Ordinal)
            || metadata.Manifest.Frontend is null)
        {
            throw Failure("package.frontend_not_found", "Package frontend is not available.");
        }

        var versionRoot = Path.GetFullPath(Path.Combine(this.packageRoot, packageId, "versions", version));
        var modulePath = Path.GetFullPath(Path.Combine(versionRoot, metadata.Manifest.Frontend.ModulePath));
        if (!modulePath.StartsWith(versionRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(modulePath))
        {
            throw Failure("package.frontend_not_found", "Package frontend is not available.");
        }

        var info = new FileInfo(modulePath);
        if (info.Length is <= 0 or > MaximumFrontendBytes)
        {
            throw Failure("package.frontend_invalid", "Package frontend module has an invalid size.");
        }

        var content = await File.ReadAllBytesAsync(modulePath, cancellationToken).ConfigureAwait(false);
        if (content.Length is <= 0 or > MaximumFrontendBytes)
        {
            throw Failure("package.frontend_invalid", "Package frontend module has an invalid size.");
        }

        var actualDigest = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actualDigest),
            Convert.FromHexString(metadata.Manifest.Frontend.Sha256)))
        {
            throw Failure("package.frontend_digest_mismatch", "Package frontend integrity verification failed.");
        }

        return new DesktopPackageFrontend(packageId, version, actualDigest, content);
    }

    private Task<string[]> EnabledPackageIdsAsync(CancellationToken cancellationToken) => this.context.PackageInstallations
        .AsNoTracking()
        .Where(row => row.State == PackageInstallationState.Enabled)
        .Select(row => row.PackageId)
        .ToArrayAsync(cancellationToken);

    private async Task<InstalledPackageMetadata> ReadMetadataAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(this.packageRoot, packageId, "state.json");
        if (!File.Exists(path))
        {
            throw Failure("package.metadata_invalid", "Package metadata is unavailable.");
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return await JsonSerializer.DeserializeAsync<InstalledPackageMetadata>(
            stream,
            this.jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw Failure("package.metadata_invalid", "Package metadata is invalid.");
    }

    private static string WidgetKey(string packageId, string stableKey) => $"{packageId}:{stableKey}";

    private static ViewportClass ParseViewport(string value) => value switch
    {
        "desktop" => ViewportClass.Desktop,
        "tablet" => ViewportClass.Tablet,
        "mobile" => ViewportClass.Mobile,
        _ => throw Failure("application.viewport_invalid", "Viewport must be desktop, tablet or mobile."),
    };

    private static string ViewportName(ViewportClass value) => value switch
    {
        ViewportClass.Desktop => "desktop",
        ViewportClass.Tablet => "tablet",
        ViewportClass.Mobile => "mobile",
        _ => throw new InvalidOperationException($"Unsupported viewport '{value}'."),
    };

    private static string PolicyName(ApplicationInstancePolicy value) => value switch
    {
        ApplicationInstancePolicy.SingleInstancePerUser => "single-instance-per-user",
        ApplicationInstancePolicy.SingleInstancePerTarget => "single-instance-per-target",
        ApplicationInstancePolicy.MultipleInstances => "multiple-instances",
        _ => throw new InvalidOperationException($"Unsupported application instance policy '{value}'."),
    };

    private static PackageManagementException Failure(string code, string message) => new(code, message);
}
