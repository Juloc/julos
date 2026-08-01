namespace JulOS.Domain.Applications;

/// <summary>
/// How many windows of one application may exist at the same time.
/// </summary>
/// <remarks>
/// The window manager consults this before opening a window: opening a
/// single-instance application focuses the window that already exists instead of
/// creating a second one.
/// </remarks>
public enum ApplicationInstancePolicy
{
    /// <summary>One window per user, whatever the target. Opening it again focuses the existing window.</summary>
    SingleInstancePerUser = 1,

    /// <summary>One window per target resource, so two different targets may be open side by side.</summary>
    SingleInstancePerTarget = 2,

    /// <summary>Any number of windows, including several for the same target.</summary>
    MultipleInstances = 3,
}
