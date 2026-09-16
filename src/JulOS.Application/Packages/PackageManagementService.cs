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
/// <param name="ArtifactDigest">Verified SHA-256 digest of the complete signed package archive.</param>
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
/// <param name="Artifact">Complete package archive stream.</param>
/// <param name="Signature">Publisher signature over the exact package archive bytes.</param>
/// <param name="ExpectedDigest">Optional expected SHA-256 digest of the complete package archive.</param>
/// <param name="PublisherId">Claimed publisher identity, or empty when the artifact is unsigned.</param>
/// <param name="PublisherKeyId">Claimed publisher key identity, or empty when unsigned.</param>
/// <param name="OperationKey">Per-caller idempotency key.</param>
/// <param name="PublisherPublicKeySpki">
/// Base64 SubjectPublicKeyInfo supplied with the upload, used only when this installation has
/// no configured key for the claimed identity. It can produce an unknown-signed result and
/// never a trusted one.
/// </param>
/// <param name="AcknowledgementDigest">
/// The acknowledgement digest a preview produced, required whenever the artifact is not
/// trusted-signed or declares rights that have to be disclosed.
/// </param>
public sealed record PackageInstallInput(
    Stream Artifact,
    byte[] Signature,
    string? ExpectedDigest,
    string PublisherId,
    string PublisherKeyId,
    string OperationKey,
    string? PublisherPublicKeySpki = null,
    string? AcknowledgementDigest = null);

/// <summary>What installing one uploaded artifact would mean, without installing it.</summary>
/// <param name="PackageId">The package identity the manifest declares.</param>
/// <param name="Version">The version the manifest declares.</param>
/// <param name="ArtifactDigest">The verified artifact digest.</param>
/// <param name="SignatureState">How much is known about who produced it.</param>
/// <param name="PublisherId">Who claimed to produce it, or null when unsigned.</param>
/// <param name="KeyId">Which key signed it, or null when unsigned.</param>
/// <param name="PublicKeyFingerprint">The fingerprint of that key, or null when unsigned.</param>
/// <param name="Permissions">The permissions the manifest declares, ordinally sorted.</param>
/// <param name="RuntimeKind">The worker runtime kind the manifest declares.</param>
/// <param name="NetworkAccess">Whether the worker requests network access.</param>
/// <param name="CriticalRightsDigest">The digest over the declared rights.</param>
/// <param name="Warnings">Stable warning codes, ordinally sorted.</param>
/// <param name="RequiresIsolation">Whether the package would run on the isolated path.</param>
/// <param name="AcknowledgementRequired">Whether installing requires the acknowledgement below.</param>
/// <param name="AcknowledgementDigest">The digest to send back with the install.</param>
/// <param name="AlreadyInstalled">Whether a package with this identity is already installed.</param>
public sealed record PackageInstallPreview(
    string PackageId,
    string Version,
    string ArtifactDigest,
    string SignatureState,
    string? PublisherId,
    string? KeyId,
    string? PublicKeyFingerprint,
    IReadOnlyList<string> Permissions,
    string RuntimeKind,
    bool NetworkAccess,
    string CriticalRightsDigest,
    IReadOnlyList<string> Warnings,
    bool RequiresIsolation,
    bool AcknowledgementRequired,
    string AcknowledgementDigest,
    bool AlreadyInstalled);

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

    /// <summary>
    /// Reports what installing one uploaded artifact would mean, changing nothing.
    /// </summary>
    /// <remarks>
    /// The preview mutates nothing and stores nothing. Its acknowledgement is a digest over
    /// the whole assessment and the operation it is for, so the install recomputes it from
    /// what was actually uploaded rather than trusting what the caller says it approved.
    /// </remarks>
    Task<PackageInstallPreview> PreviewAsync(
        PackageInstallInput input,
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

    /// <summary>
    /// Restarts the worker of every enabled package after a host restart.
    /// Worker processes do not survive a restart, so this reconciles the running
    /// worker set with the enabled state persisted in the database. It is
    /// best-effort per package and never throws for an individual worker.
    /// </summary>
    Task StartEnabledWorkersAsync(CancellationToken cancellationToken = default);
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
