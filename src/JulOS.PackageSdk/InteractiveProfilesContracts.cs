using System.Text.Json;

namespace JulOS.PackageSdk;

/// <summary>
/// Stable capability for managing the calling package's own interactive-session
/// profiles and network profiles. Core stays product-neutral: it forwards an
/// opaque package-defined payload to the caller's own worker and returns the
/// worker's caller-safe response.
/// </summary>
public static class InteractiveProfilesCapabilityContract
{
    /// <summary>Capability identity.</summary>
    public const string Name = "interactive.profiles";

    /// <summary>Current capability version.</summary>
    public const string Version = "1.0.0";

    /// <summary>Creates one package-wide network profile.</summary>
    public const string CreateNetworkOperation = "create-network";

    /// <summary>Lists package-wide network profiles.</summary>
    public const string ListNetworksOperation = "list-networks";

    /// <summary>Creates one profile owned by the authenticated user.</summary>
    public const string CreateOperation = "create";

    /// <summary>Lists the authenticated user's own profiles.</summary>
    public const string ListOperation = "list";

    /// <summary>Deletes one profile owned by the authenticated user.</summary>
    public const string DeleteOperation = "delete";
}

/// <summary>Private worker commands Core uses to manage a package's interactive profiles.</summary>
public static class InteractiveProfilesWorkerCommands
{
    /// <summary>Creates one package-wide network profile.</summary>
    public const string CreateNetworkProfile = "interactive.profiles.create-network";

    /// <summary>Lists package-wide network profiles.</summary>
    public const string ListNetworkProfiles = "interactive.profiles.list-networks";

    /// <summary>Creates one user-owned profile.</summary>
    public const string CreateProfile = "interactive.profiles.create";

    /// <summary>Lists the owning user's profiles.</summary>
    public const string ListProfiles = "interactive.profiles.list";

    /// <summary>Deletes one user-owned profile.</summary>
    public const string DeleteProfile = "interactive.profiles.delete";
}

/// <summary>
/// Trusted profile-management request delivered by Core only to the caller's own
/// package worker. Core sets the authenticated owning user; the package request
/// stays opaque so no product-specific profile shape enters Core.
/// </summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="Request">Opaque package-defined profile request.</param>
public sealed record ManageInteractiveProfilesRequest(
    Guid OwnerUserId,
    JsonElement Request);
