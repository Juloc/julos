namespace JulOS.Application.Packages;

/// <summary>One enabled package application that can be composed into the JulOS Desktop.</summary>
public sealed record DesktopPackageApplication(
    Guid ApplicationDefinitionId,
    string PackageId,
    string PackageVersion,
    string StableKey,
    string DisplayNameKey,
    string InstancePolicy,
    int DefaultWidth,
    int DefaultHeight,
    int MinimumWidth,
    int MinimumHeight,
    IReadOnlyList<string> Viewports,
    string ElementName,
    string FrontendSha256,
    IReadOnlyList<string> FrontendExportedElements);

/// <summary>One enabled package widget that can be placed on the JulOS Desktop grid.</summary>
public sealed record DesktopPackageWidget(
    string WidgetKey,
    string PackageId,
    string PackageVersion,
    string StableKey,
    string DisplayNameKey,
    string ElementName,
    IReadOnlyList<string> Sizes,
    string DefaultSize,
    string FrontendSha256,
    IReadOnlyList<string> FrontendExportedElements);

/// <summary>Verified frontend module bytes from one enabled installed package.</summary>
public sealed record DesktopPackageFrontend(
    string PackageId,
    string Version,
    string Sha256,
    byte[] Content);

/// <summary>Read-only desktop projection of enabled package applications, widgets and signed frontend modules.</summary>
public interface IDesktopApplicationCatalog
{
    /// <summary>Lists enabled package applications for one supported viewport.</summary>
    /// <param name="viewport">Viewport identity such as desktop, tablet or mobile.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Launchable applications visible to the Desktop shell.</returns>
    Task<IReadOnlyList<DesktopPackageApplication>> ListAsync(
        string viewport,
        CancellationToken cancellationToken = default);

    /// <summary>Lists widgets declared by enabled packages.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Widgets that may be resolved by stored desktop placements.</returns>
    Task<IReadOnlyList<DesktopPackageWidget>> ListWidgetsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads and verifies the signed frontend module for one enabled installed package version.</summary>
    /// <param name="packageId">Stable package identity.</param>
    /// <param name="version">Exact installed package version.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>Verified frontend module bytes and digest.</returns>
    Task<DesktopPackageFrontend> ReadFrontendAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
