namespace JulOS.Contracts.Packages;

/// <summary>How much is known about who produced an installed package.</summary>
/// <remarks>
/// The same names the catalog uses for the same distinction, because an administrator
/// reading one surface after the other should not have to learn two vocabularies for it.
/// A package never persists an invalid signature: a claim that cannot be proved fails the
/// install rather than becoming a state.
/// </remarks>
public static class PackageSignatureStateNames
{
    /// <summary>Signed by a key this installation is configured to trust.</summary>
    public const string TrustedSigned = "trusted-signed";

    /// <summary>Cryptographically valid, but signed by a key this installation does not trust.</summary>
    public const string UnknownSigned = "unknown-signed";

    /// <summary>Carries no publisher signature at all.</summary>
    /// <remarks>Named NotSigned rather than Unsigned so it is not read as a numeric type.</remarks>
    public const string NotSigned = "unsigned";

    /// <summary>Every package signature state name.</summary>
    public static IReadOnlyList<string> All { get; } = [TrustedSigned, UnknownSigned, NotSigned];
}

/// <summary>Current state of one installed package.</summary>
/// <param name="InstallationId">Installation identity.</param>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="Version">Installed package version.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="FaultCode">Stable fault code when faulted.</param>
/// <param name="FaultDetail">Caller-safe fault detail.</param>
/// <param name="FaultedAtUtc">Time the fault was recorded.</param>
/// <param name="ConfigurationRequired">Whether valid configuration is still required.</param>
/// <param name="WorkerHealthy">Whether the package worker is currently healthy.</param>
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
/// <param name="Values">Validated non-secret configuration values.</param>
public sealed record ConfigurePackageRequest(
    int Revision,
    IReadOnlyDictionary<string, string> Values);

/// <summary>Requests one revision-protected package lifecycle transition.</summary>
/// <param name="Revision">Expected package revision.</param>
public sealed record PackageRevisionRequest(int Revision);

/// <summary>Removes a package and optionally destroys its isolated data.</summary>
/// <param name="Revision">Expected package revision.</param>
/// <param name="DeletePackageData">Whether package-owned data is permanently deleted.</param>
public sealed record RemovePackageRequest(int Revision, bool DeletePackageData);

/// <summary>Describes the effects and rollback risk of one package update.</summary>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="CurrentVersion">Currently installed version.</param>
/// <param name="TargetVersion">Candidate target version.</param>
/// <param name="NewMigrations">Migrations introduced by the target artifact.</param>
/// <param name="IrreversibleMigrations">New migrations that cannot be rolled back automatically.</param>
/// <param name="RequiresExplicitApproval">Whether the update requires explicit irreversible-change approval.</param>
public sealed record PackageUpdatePreviewResponse(
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    IReadOnlyList<string> NewMigrations,
    IReadOnlyList<string> IrreversibleMigrations,
    bool RequiresExplicitApproval);
