using JulOS.Domain.Primitives;

namespace JulOS.Domain.Devices;

/// <summary>The generated identity of one registered client device.</summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct ClientDeviceId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}

/// <summary>
/// One browser or installed PWA instance a user has registered, used only to resolve
/// layout preferences.
/// </summary>
/// <remarks>
/// <para>
/// A client device is explicitly not an authentication factor. Its key identifies which
/// layout preferences to load and nothing else: losing the device cookie must not end the
/// user's session, and holding one must never grant access to any other resource. Every
/// device record is scoped to the authenticated user who created it.
/// </para>
/// <para>
/// The device is identified by a server-generated random key of which only a hash is
/// stored. Clearing site data therefore produces a visibly new device rather than silently
/// re-adopting an existing one, which is the behaviour <c>docs/MOBILE_PWA.md</c> section 3
/// requires.
/// </para>
/// </remarks>
public sealed class ClientDevice
{
    private readonly List<DeviceWorkspacePreference> preferences = [];

    private ClientDevice(
        ClientDeviceId id,
        Guid ownerUserId,
        string clientInstanceKeyHash,
        string displayName,
        WorkspaceClass lastDetectedWorkspaceClass,
        DateTimeOffset createdAtUtc)
    {
        this.Id = id;
        this.OwnerUserId = ownerUserId;
        this.ClientInstanceKeyHash = clientInstanceKeyHash;
        this.DisplayName = displayName;
        this.LastDetectedWorkspaceClass = lastDetectedWorkspaceClass;
        this.CreatedAtUtc = createdAtUtc;
        this.LastSeenAtUtc = createdAtUtc;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated device identity.</summary>
    public ClientDeviceId Id { get; }

    /// <summary>The user this device belongs to. Never crosses users.</summary>
    public Guid OwnerUserId { get; }

    /// <summary>The hash of the server-generated client instance key. The key itself is never stored.</summary>
    public string ClientInstanceKeyHash { get; }

    /// <summary>The user-facing device name.</summary>
    public string DisplayName { get; private set; }

    /// <summary>The workspace class most recently detected on this device.</summary>
    public WorkspaceClass LastDetectedWorkspaceClass { get; private set; }

    /// <summary>The workspace class this device is pinned to, if any.</summary>
    /// <remarks>
    /// A device can never be pinned to <see cref="WorkspaceClass.DesktopMulti"/>, which is
    /// entered only through the explicit Multi-Display controller.
    /// </remarks>
    public WorkspaceClass? WorkspaceClassOverride { get; private set; }

    /// <summary>When the device was registered.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>When the device was last seen.</summary>
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>The per-workspace layout preferences of this device.</summary>
    public IReadOnlyList<DeviceWorkspacePreference> Preferences => this.preferences;

    /// <summary>Registers a new client device for an authenticated user.</summary>
    /// <param name="id">Generated device identity.</param>
    /// <param name="ownerUserId">The authenticated user.</param>
    /// <param name="clientInstanceKeyHash">Hash of the server-generated device key.</param>
    /// <param name="displayName">User-facing device name.</param>
    /// <param name="detectedWorkspaceClass">The workspace class the client reported.</param>
    /// <param name="createdAtUtc">Registration time.</param>
    /// <exception cref="DomainRuleViolationException">An argument does not identify a valid device.</exception>
    public static ClientDevice Register(
        ClientDeviceId id,
        Guid ownerUserId,
        string clientInstanceKeyHash,
        string displayName,
        WorkspaceClass detectedWorkspaceClass,
        DateTimeOffset createdAtUtc)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "client_device.not_owned",
                "A client device belongs to an authenticated user.");
        }

        return new ClientDevice(
            id,
            ownerUserId,
            RequireKeyHash(clientInstanceKeyHash),
            RequireDisplayName(displayName),
            RequireDetectable(detectedWorkspaceClass),
            createdAtUtc);
    }

    /// <summary>Renames the device.</summary>
    public void Rename(string displayName)
    {
        this.DisplayName = RequireDisplayName(displayName);
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Records the workspace class the client detected.
    /// </summary>
    /// <remarks>
    /// Detection is an observation, not a user decision, so it does not advance the
    /// revision: a resize must never make a concurrent preference write fail.
    /// </remarks>
    public void RecordDetectedWorkspace(WorkspaceClass detected, DateTimeOffset seenAtUtc)
    {
        this.LastDetectedWorkspaceClass = RequireDetectable(detected);
        this.LastSeenAtUtc = seenAtUtc;
    }

    /// <summary>Pins the device to a workspace class, or clears the pin.</summary>
    /// <exception cref="DomainRuleViolationException">The override is not a pinnable class.</exception>
    public void OverrideWorkspace(WorkspaceClass? workspaceClass)
    {
        if (workspaceClass is not null)
        {
            _ = RequireDetectable(workspaceClass.Value);
        }

        this.WorkspaceClassOverride = workspaceClass;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Sets this device's preference for one workspace class.</summary>
    public void SetPreference(WorkspaceClass workspaceClass, LayoutScope scope, RestoreMode restoreMode)
    {
        var existing = this.preferences.FindIndex(preference => preference.WorkspaceClass == workspaceClass);
        var updated = new DeviceWorkspacePreference(workspaceClass, scope, restoreMode);

        if (existing >= 0)
        {
            this.preferences[existing] = updated;
        }
        else
        {
            this.preferences.Add(updated);
        }

        this.Revision = this.Revision.Next();
    }

    /// <summary>Restores a persisted device without advancing its revision.</summary>
    public static ClientDevice Restore(
        ClientDeviceId id,
        Guid ownerUserId,
        string clientInstanceKeyHash,
        string displayName,
        WorkspaceClass lastDetectedWorkspaceClass,
        WorkspaceClass? workspaceClassOverride,
        DateTimeOffset createdAtUtc,
        DateTimeOffset lastSeenAtUtc,
        Revision revision,
        IEnumerable<DeviceWorkspacePreference> preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var device = new ClientDevice(
            id,
            ownerUserId,
            clientInstanceKeyHash,
            displayName,
            lastDetectedWorkspaceClass,
            createdAtUtc)
        {
            WorkspaceClassOverride = workspaceClassOverride,
            LastSeenAtUtc = lastSeenAtUtc,
            Revision = revision,
        };
        device.preferences.AddRange(preferences);
        return device;
    }

    private static WorkspaceClass RequireDetectable(WorkspaceClass workspaceClass) => workspaceClass switch
    {
        WorkspaceClass.Phone or WorkspaceClass.Tablet or WorkspaceClass.DesktopSingle => workspaceClass,
        WorkspaceClass.DesktopMulti => throw new DomainRuleViolationException(
            "client_device.workspace_preference_invalid",
            "Multi-display is entered through the Multi-Display controller, so a device cannot be pinned to it."),
        _ => throw new DomainRuleViolationException(
            "desktop.workspace_class_invalid",
            "The workspace class is not supported."),
    };

    private static string RequireDisplayName(string displayName)
    {
        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 128)
        {
            throw new DomainRuleViolationException(
                "client_device.invalid",
                "A device name has 1 to 128 characters.");
        }
        return trimmed;
    }

    private static string RequireKeyHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64)
        {
            throw new DomainRuleViolationException(
                "client_device.invalid",
                "A client instance key hash is a 64-character SHA-256 hex value.");
        }
        return hash;
    }
}

/// <summary>One device's layout preference for a single workspace class.</summary>
/// <param name="WorkspaceClass">The workspace class this preference applies to.</param>
/// <param name="Scope">Whether the workspace loads the shared or the device layout.</param>
/// <param name="RestoreMode">Whether the workspace restores windows or starts fresh.</param>
public readonly record struct DeviceWorkspacePreference(
    WorkspaceClass WorkspaceClass,
    LayoutScope Scope,
    RestoreMode RestoreMode);
