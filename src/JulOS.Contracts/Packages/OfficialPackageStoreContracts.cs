namespace JulOS.Contracts.Packages;

/// <summary>One official package exposed by the JulOS package store.</summary>
public sealed record OfficialPackageStoreResponse(
    string PackageId,
    string Version,
    string DisplayNameEn,
    string DisplayNameDe,
    string DescriptionEn,
    string DescriptionDe,
    string? InstalledVersion,
    string? InstalledState,
    int? InstalledRevision,
    bool UpdateAvailable);
