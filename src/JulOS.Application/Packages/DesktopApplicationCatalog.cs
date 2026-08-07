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

/// <summary>Verified frontend module bytes from one enabled installed package.</summary>
public sealed record DesktopPackageFrontend(
    string PackageId,
    string Version,
    string Sha256,
    byte[] Content);

/// <summary>Read-only desktop projection of enabled package applications and their signed frontend modules.</summary>
public interface IDesktopApplicationCatalog
{
    Task<IReadOnlyList<DesktopPackageApplication>> ListAsync(
        string viewport,
        CancellationToken cancellationToken = default);

    Task<DesktopPackageFrontend> ReadFrontendAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default);
}
