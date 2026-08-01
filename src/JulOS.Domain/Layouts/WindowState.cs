namespace JulOS.Domain.Layouts;

/// <summary>
/// How a window is currently presented on the desktop.
/// </summary>
/// <remarks>
/// This is presentation state only. It is never evidence that a runtime session behind
/// the window is still alive; that is what the session reference is for.
/// </remarks>
public enum WindowState
{
    /// <summary>Freely positioned and sized.</summary>
    Normal = 1,

    /// <summary>Hidden from the desktop but still open and listed in the taskbar.</summary>
    Minimized = 2,

    /// <summary>Filling the usable desktop area.</summary>
    Maximized = 3,

    /// <summary>Filling the left half of the usable area.</summary>
    SnappedLeft = 4,

    /// <summary>Filling the right half of the usable area.</summary>
    SnappedRight = 5,

    /// <summary>Filling the upper-left quarter of the usable area.</summary>
    SnappedTopLeft = 6,

    /// <summary>Filling the upper-right quarter of the usable area.</summary>
    SnappedTopRight = 7,

    /// <summary>Filling the lower-left quarter of the usable area.</summary>
    SnappedBottomLeft = 8,

    /// <summary>Filling the lower-right quarter of the usable area.</summary>
    SnappedBottomRight = 9,

    /// <summary>Filling the whole viewport, without desktop chrome.</summary>
    FullScreen = 10,
}
