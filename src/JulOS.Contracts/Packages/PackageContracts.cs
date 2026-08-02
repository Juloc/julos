namespace JulOS.Contracts.Packages;

public sealed record PackageInstallationResponse(
    Guid InstallationId,
    string PackageId,
    string Version,
    string State,
    int Revision,
    string? FaultCode,
    string? FaultDetail,
    DateTimeOffset? FaultedAtUtc,
    bool ConfigurationRequired,
    bool WorkerHealthy,
    string ArtifactDigest);

public sealed record ConfigurePackageRequest(
    int Revision,
    IReadOnlyDictionary<string, string> Values);

public sealed record PackageRevisionRequest(int Revision);

public sealed record RemovePackageRequest(int Revision, bool DeletePackageData);

public sealed record PackageUpdatePreviewResponse(
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    IReadOnlyList<string> NewMigrations,
    IReadOnlyList<string> IrreversibleMigrations,
    bool RequiresExplicitApproval);
