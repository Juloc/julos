namespace JulOS.Domain.Primitives;

/// <summary>
/// The class of Shell presentation a desktop layout belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Workspace class is the persisted presentation identity from <c>docs/MOBILE_PWA.md</c>
/// section 2. It is deliberately separate from <see cref="ViewportClass"/>, which is the
/// compatibility an application declares: several workspace classes map onto the same
/// application viewport class, and a device may be pinned to a workspace class without
/// changing what any application supports.
/// </para>
/// <para>
/// <see cref="DesktopMulti"/> is never produced by automatic classification. It is
/// entered only through the explicit Multi-Display controller with at least two active
/// display participants, which is why a client device can never be pinned to it.
/// </para>
/// </remarks>
public enum WorkspaceClass
{
    /// <summary>A narrow touch workspace showing one or two foreground surfaces.</summary>
    Phone = 1,

    /// <summary>A touch workspace that shows several surfaces at once.</summary>
    Tablet = 2,

    /// <summary>A pointer-driven workspace on one display.</summary>
    DesktopSingle = 3,

    /// <summary>A pointer-driven workspace spanning several coordinated displays.</summary>
    DesktopMulti = 4,
}

/// <summary>Whether a layout is shared by the user or bound to one client device.</summary>
public enum LayoutScope
{
    /// <summary>One layout per workspace class, shared by every device of the user.</summary>
    Shared = 1,

    /// <summary>A layout private to one registered client device.</summary>
    Device = 2,
}

/// <summary>Whether a workspace restores its windows or starts empty.</summary>
public enum RestoreMode
{
    /// <summary>Restore the persisted windows.</summary>
    Resume = 1,

    /// <summary>Start with no restored windows and persist no window state.</summary>
    Fresh = 2,
}

/// <summary>What happens to a frontend surface that leaves the visible foreground.</summary>
/// <remarks>
/// The default is <see cref="Suspend"/>. Only a user can choose to keep a surface active;
/// an application can neither request nor persist a mode for itself.
/// </remarks>
public enum BackgroundMode
{
    /// <summary>Stop timers, polling, rendering and display connections.</summary>
    Suspend = 1,

    /// <summary>Keep the surface running while the JulOS page is alive; best effort.</summary>
    KeepSurfaceActive = 2,
}
