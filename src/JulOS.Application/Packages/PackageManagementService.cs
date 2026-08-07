namespace JulOS.Application.Packages;

/// <summary>Authoritative package installation state used by application services.</summary>
/// <param name="InstallationId">Installation identity.</param>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="Version">Installed semantic version.</param>
/// <param name="State">Current package lifecycle state.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
/// <param name="FaultCode">Stable fault code when faulted.</param>
/// <param name="FaultDetail">Caller-safe fault detail.</param>
/// <param name="FaultedAtUtc">Fault observation time.</param>
/// <param name="ConfigurationRequired">Whether configuration is required before enablement.</param>
/// <param name="WorkerHealthy">Whether the package worker is currently healthy.</param>
/// <param name="ArtifactDigest">Verified SHA-256 digest of the signed package manifest.</param>
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
/// <param name="Artifact">Package archive stream containing the signed manifest.</param>
/// <param name="Signature">Publisher signature over the exact manifest bytes.</param>
/// <param name="ExpectedDigest">Optional expected SHA-256 digest of the signed manifest.</param>
/// <param name="PublisherId">Trusted publisher identity.</param>
/// <param name="PublisherKeyId">Trusted publisher key identity.</param>
/// <param name="OperationKey">Per-caller idempotency key.</param>
public sealed record PackageInstallInput(
    Stream Artifact,
    byte[] Signature,
    string? ExpectedDigest,
    string PublisherId,
    string PublisherKeyId,
    string OperationKey);

/// <summary>Package configuration values and expected revision.</summary>
/// <param name="Values">Validated non-secret configuration values.</param>
/// <param name="Revision">Expected package revision.</param>
public sealed record PackageConfigurationInput(
    IReadOnlyDictionary<string, string> Values,
    int Revision);

/// <summary>Package removal options.</summary>
/// <param name="Revision">Expected package revision.</param>
/// <param name="DeletePackageData">Whether isolated package data is permanently deleted.</param>
public sealed record PackageRemovalInput(
    int Revision,
    bool DeletePackageData);

/// <summary>Application boundary for package install, configuration and lifecycle transitions.</summary>
public interface IPackageManagementService
{
    /// <summary>Lists all package installations.</summary>
    Task<IReadOnlyList<PackageInstallationSnapshot>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Verifies and installs one package idempotently.</summary>
    Task<PackageInstallationSnapshot> InstallAsync(
        PackageInstallInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Validates and applies package configuration.</summary>
    Task<PackageInstallationSnapshot> ConfigureAsync(
        string packageId,
        PackageConfigurationInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Enables one configured healthy package.</summary>
    Task<PackageInstallationSnapshot> EnableAsync(
        string packageId,
        int revision,
        CancellationToken cancellationToken = default);

    /// <summary>Disables one package without destroying its installation or data.</summary>
    Task<PackageInstallationSnapshot> DisableAsync(
        string packageId,
        int revision,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one package and optionally destroys its isolated data.</summary>
    Task<PackageInstallationSnapshot> RemoveAsync(
        string packageId,
        PackageRemovalInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable caller-safe package management failure.</summary>
public sealed class PackageManagementException : Exception
{
    /// <summary>Creates a package management failure.</summary>
    /// <param name="code">Stable machine-readable failure code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    /// <param name="innerException">Optional server-side cause.</param>
    public PackageManagementException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable failure code.</summary>
    public string Code { get; }
}
