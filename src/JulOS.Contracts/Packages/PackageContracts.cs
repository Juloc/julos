namespace JulOS.Contracts.Packages;

/// <summary>Current state of one installed package.</summary>
/// <param name="InstallationId">Installation identity.</param>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="Version">Installed version.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="FaultCode">Stable fault code when faulted.</param>
/// <param name="FaultDetail">Caller-safe fault detail.</param>
/// <param name="FaultedAtUtc">Time the fault was recorded.</param>
/// <param name="ConfigurationRequired">Whether valid configuration is still required.</param>
/// <param name="WorkerHealthy">Whether the worker is currently healthy.</param>
/// <param name="ArtifactDigest">Verified package artifact digest.</param>
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

/// <summary>Validates and applies package configuration.</summary>
/// <param name="Revision">Expected package revision.</param>
/// <param name="Values">Non-secret configuration values.</param>
public sealed record ConfigurePackageRequest(
    int Revision,
    IReadOnlyDictionary<string, string> Values);

/// <summary>Requests a revision-protected package lifecycle action.</summary>
/// <param name="Revision">Expected package revision.</param>
public sealed record PackageRevisionRequest(int Revision);

/// <summary>Removes a package and optionally its isolated data.</summary>
/// <param name="Revision">Expected package revision.</param>
/// <param name="DeletePackageData">Whether the package-owned database schema is deleted.</param>
public sealed record RemovePackageRequest(int Revision, bool DeletePackageData);
