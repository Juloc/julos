namespace JulOS.Application.Packages;

public sealed record PackageUpdateInput(
    Stream Artifact,
    byte[] Signature,
    string ExpectedDigest,
    string PublisherId,
    string PublisherKeyId,
    int Revision,
    bool AllowIrreversibleMigrations);

public sealed record PackageUpdatePreview(
    string PackageId,
    string CurrentVersion,
    string TargetVersion,
    IReadOnlyList<string> NewMigrations,
    IReadOnlyList<string> IrreversibleMigrations,
    bool RequiresExplicitApproval);

public interface IPackageUpdateService
{
    Task<PackageUpdatePreview> PreviewAsync(
        string packageId,
        PackageUpdateInput input,
        CancellationToken cancellationToken = default);

    Task<PackageInstallationSnapshot> UpdateAsync(
        string packageId,
        PackageUpdateInput input,
        CancellationToken cancellationToken = default);
}
