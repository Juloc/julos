namespace JulOS.Domain.Packages;

/// <summary>
/// The lifecycle state of one package installation record.
/// </summary>
/// <remarks>
/// The valid moves between these states are enforced by <see cref="PackageInstallation"/>.
/// </remarks>
public enum PackageInstallationState
{
    /// <summary>The artifact is being verified and package storage is being created.</summary>
    Installing,

    /// <summary>Artifact and storage exist. The worker is not active.</summary>
    Installed,

    /// <summary>An administrator is providing or validating required settings.</summary>
    Configuring,

    /// <summary>Installed and intentionally inactive. Configuration remains.</summary>
    Disabled,

    /// <summary>The worker is being started and validated.</summary>
    Starting,

    /// <summary>The worker is ready and its registrations are active.</summary>
    Enabled,

    /// <summary>The worker is being stopped.</summary>
    Stopping,

    /// <summary>A new artifact is being verified, migrated and activated.</summary>
    Updating,

    /// <summary>Worker, migration, configuration or dependency state prevents normal function. Core remains available.</summary>
    Faulted,

    /// <summary>The runtime is stopped and selected package resources are being removed.</summary>
    Removing,
}
