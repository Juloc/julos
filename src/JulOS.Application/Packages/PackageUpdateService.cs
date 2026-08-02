namespace JulOS.Application.Packages;

/// <summary>Verified candidate package update and approval options.</summary>
/// <param name="Artifact">Candidate package artifact stream.</param>
/// <param name="Signature">Publisher signature bytes.</param>
/// <param name="ExpectedDigest">Expected SHA-256 artifact digest.</param>
/// <param name="PublisherId">Trusted publisher identity.</param>
/// <param name="PublisherKeyId">Trusted publisher key identity.</param>
/// <param name="Revision">Expected installed package revision.</param>
/// <param name="AllowIrreversibleMigrations">Whether irreversible migrations are explicitly approved.</param>
public sealed record PackageUpdateInput(
    Stream Artifact,
    byte[] Signature,
    string ExpectedDigest,
    string PublisherId,
    string PublisherKeyId,
    int Revision,
    bool AllowIrreversibleMigrations);

/// <summary>Effects and migration risk calculated before applying an update.</summary>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="CurrentVersion">Currently installed version.</param>
/// <param name="TargetVersion">Candidate target version.</param>
/// <param name="NewMigrations">Migrations introduced by the candidate.</param>
/// <param name="IrreversibleMigrations">New migrations that cannot be rolled back automatically.</param>
/// <param name="RequiresExplicitApproval">Whether explicit irreversible-change approval is required.</param>
public sealed record PackageUpdatePreview(
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    IReadOnlyList<string> NewMigrations,
    IReadOnlyList<string> IrreversibleMigrations,
    bool RequiresExplicitApproval);

/// <summary>Application boundary for package update preview and execution.</summary>
public interface IPackageUpdateService
{
    /// <summary>Verifies a candidate and calculates migrations without changing installation state.</summary>
    Task<PackageUpdatePreview> PreviewAsync(
        string packageId,
        PackageUpdateInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one verified revision-protected package update with bounded rollback.</summary>
    Task<PackageInstallationSnapshot> UpdateAsync(
        string packageId,
        PackageUpdateInput input,
        CancellationToken cancellationToken = default);
}
