namespace JulOS.Application.Packages;

/// <summary>Authoritative package installation state used by application services.</summary>
public sealed record PackageInstallationSnapshot(
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

/// <summary>Verified package installation input.</summary>
public sealed record PackageInstallInput(
    Stream Artifact,
    byte[] Signature,
    string ExpectedDigest,
    string PublisherId,
    string PublisherKeyId,
    string OperationKey);

/// <summary>Package configuration values and expected revision.</summary>
public sealed record PackageConfigurationInput(
    IReadOnlyDictionary<string, string> Values,
    int Revision);

/// <summary>Package removal options.</summary>
public sealed record PackageRemovalInput(
    int Revision,
    bool DeletePackageData);

/// <summary>Application boundary for package install, configuration and lifecycle transitions.</summary>
public interface IPackageManagementService
{
    Task<IReadOnlyList<PackageInstallationSnapshot>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<PackageInstallationSnapshot> InstallAsync(
        PackageInstallInput input,
        CancellationToken cancellationToken = default);

    Task<PackageInstallationSnapshot> ConfigureAsync(
        string packageId,
        PackageConfigurationInput input,
        CancellationToken cancellationToken = default);

    Task<PackageInstallationSnapshot> EnableAsync(
        string packageId,
        int revision,
        CancellationToken cancellationToken = default);

    Task<PackageInstallationSnapshot> DisableAsync(
        string packageId,
        int revision,
        CancellationToken cancellationToken = default);

    Task<PackageInstallationSnapshot> RemoveAsync(
        string packageId,
        PackageRemovalInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable caller-safe package management failure.</summary>
public sealed class PackageManagementException : Exception
{
    public PackageManagementException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    public string Code { get; }
}
