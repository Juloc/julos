namespace JulOS.Contracts.Devices;

/// <summary>The stable client-device and workspace error codes from <c>docs/MOBILE_PWA.md</c> section 16.</summary>
public static class ClientDeviceErrorCodes
{
    /// <summary>No device with the requested identity exists for this user.</summary>
    public const string NotFound = "client_device.not_found";

    /// <summary>The request carried no usable device cookie.</summary>
    public const string NotRegistered = "client_device.not_registered";

    /// <summary>The device belongs to a different user.</summary>
    public const string NotOwned = "client_device.not_owned";

    /// <summary>A device field failed validation.</summary>
    public const string Invalid = "client_device.invalid";

    /// <summary>A workspace preference or override is not acceptable.</summary>
    public const string WorkspacePreferenceInvalid = "client_device.workspace_preference_invalid";

    /// <summary>The workspace class is not one this release knows.</summary>
    public const string WorkspaceClassInvalid = "desktop.workspace_class_invalid";

    /// <summary>The application does not support the requested background mode.</summary>
    public const string BackgroundModeUnsupported = "application.background_mode_unsupported";
}

/// <summary>The workspace classes a client may report or be pinned to.</summary>
/// <remarks>
/// <c>desktop-multi</c> is intentionally absent from the pinnable set: it is entered only
/// through the explicit Multi-Display controller.
/// </remarks>
public static class WorkspaceClassNames
{
    /// <summary>A narrow touch workspace.</summary>
    public const string Phone = "phone";

    /// <summary>A touch workspace showing several surfaces.</summary>
    public const string Tablet = "tablet";

    /// <summary>A pointer-driven workspace on one display.</summary>
    public const string DesktopSingle = "desktop-single";

    /// <summary>A pointer-driven workspace across coordinated displays.</summary>
    public const string DesktopMulti = "desktop-multi";

    /// <summary>Every workspace class name.</summary>
    public static IReadOnlyList<string> All { get; } = [Phone, Tablet, DesktopSingle, DesktopMulti];

    /// <summary>The classes a client device may be pinned to.</summary>
    public static IReadOnlyList<string> Pinnable { get; } = [Phone, Tablet, DesktopSingle];
}

/// <summary>What the client reports when it registers or is seen again.</summary>
/// <param name="DisplayName">User-facing device name.</param>
/// <param name="DetectedWorkspaceClass">The workspace class the client classified itself as.</param>
public sealed record RegisterClientDeviceRequest(string DisplayName, string DetectedWorkspaceClass);

/// <summary>One device's preference for a single workspace class.</summary>
/// <param name="WorkspaceClass">The workspace class.</param>
/// <param name="LayoutScope">"shared" or "device".</param>
/// <param name="RestoreMode">"resume" or "fresh".</param>
public sealed record DeviceWorkspacePreferenceResponse(
    string WorkspaceClass,
    string LayoutScope,
    string RestoreMode);

/// <summary>A registered client device as its owner sees it.</summary>
/// <param name="ClientDeviceId">Opaque device identity.</param>
/// <param name="DisplayName">User-facing device name.</param>
/// <param name="LastDetectedWorkspaceClass">Most recently detected workspace class.</param>
/// <param name="WorkspaceClassOverride">Pinned workspace class, or null.</param>
/// <param name="CreatedAtUtc">Registration time.</param>
/// <param name="LastSeenAtUtc">Last contact time.</param>
/// <param name="Revision">Optimistic-concurrency revision.</param>
/// <param name="Preferences">Per-workspace preferences.</param>
/// <param name="IsCurrentDevice">Whether this is the device making the request.</param>
/// <remarks>The client instance key and its hash are never part of any response.</remarks>
public sealed record ClientDeviceResponse(
    Guid ClientDeviceId,
    string DisplayName,
    string LastDetectedWorkspaceClass,
    string? WorkspaceClassOverride,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int Revision,
    IReadOnlyList<DeviceWorkspacePreferenceResponse> Preferences,
    bool IsCurrentDevice);

/// <summary>Renames a device or changes its workspace pin.</summary>
/// <param name="DisplayName">New device name.</param>
/// <param name="WorkspaceClassOverride">New pin, or null to clear it.</param>
/// <param name="ExpectedRevision">The revision the caller based the change on.</param>
public sealed record UpdateClientDeviceRequest(
    string DisplayName,
    string? WorkspaceClassOverride,
    int ExpectedRevision);

/// <summary>Sets this device's preference for one workspace class.</summary>
/// <param name="LayoutScope">"shared" or "device".</param>
/// <param name="RestoreMode">"resume" or "fresh".</param>
/// <param name="ExpectedRevision">The revision the caller based the change on.</param>
public sealed record UpdateDeviceWorkspacePreferenceRequest(
    string LayoutScope,
    string RestoreMode,
    int ExpectedRevision);

/// <summary>The realtime event raised when a device or its preferences change.</summary>
public static class ClientDeviceEvents
{
    /// <summary>A device was registered, renamed, re-preferenced or removed.</summary>
    public const string Changed = "client_device.changed";
}
