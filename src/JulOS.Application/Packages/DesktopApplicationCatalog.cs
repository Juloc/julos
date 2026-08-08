namespace JulOS.Application.Packages;

/// <summary>One approved launch target owned by a package application.</summary>
public sealed record DesktopPackageLaunchTarget(
    Guid LaunchTargetId,
    Guid ApplicationDefinitionId,
    string ExternalIdentity,
    string DisplayName);

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
    IReadOnlyList<string> FrontendExportedElements,
    IReadOnlyList<DesktopPackageLaunchTarget> LaunchTargets);

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
    Task<IReadOnlyList<DesktopPackageApplication>> ListAsync(
        string viewport,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or updates an approved package-owned launch target.</summary>
    Task<DesktopPackageLaunchTarget> SaveLaunchTargetAsync(
        Guid userId,
        string packageId,
        string applicationStableKey,
        string externalIdentity,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one package-owned launch target.</summary>
    Task DeleteLaunchTargetAsync(
        string packageId,
        Guid launchTargetId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists widgets declared by enabled packages.</summary>
    Task<IReadOnlyList<DesktopPackageWidget>> ListWidgetsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads and verifies the signed frontend module for one enabled installed package version.</summary>
    Task<DesktopPackageFrontend> ReadFrontendAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
