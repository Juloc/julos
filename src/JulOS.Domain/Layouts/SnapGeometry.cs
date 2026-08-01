namespace JulOS.Domain.Layouts;

/// <summary>
/// Turns a snapped window state into concrete bounds.
/// </summary>
/// <remarks>
/// The calculation is pure arithmetic so the preview shown before the pointer is released
/// and the bounds stored afterwards come from the same place and cannot disagree. Halves
/// are computed so that the two sides always add up to the full edge, which keeps a
/// one-pixel gap from appearing on an odd width.
/// </remarks>
public static class SnapGeometry
{
    /// <summary>
    /// Returns the bounds a window takes in <paramref name="state"/>, or <see langword="null"/>
    /// when the state has no fixed geometry and the window keeps its own bounds.
    /// </summary>
    public static WindowBounds? BoundsFor(WindowState state, UsableArea usableArea)
    {
        var leftWidth = usableArea.Width / 2;
        var rightWidth = usableArea.Width - leftWidth;
        var topHeight = usableArea.Height / 2;
        var bottomHeight = usableArea.Height - topHeight;

        return state switch
        {
            WindowState.Maximized or WindowState.FullScreen =>
                WindowBounds.Create(0, 0, usableArea.Width, usableArea.Height),
            WindowState.SnappedLeft =>
                WindowBounds.Create(0, 0, leftWidth, usableArea.Height),
            WindowState.SnappedRight =>
                WindowBounds.Create(leftWidth, 0, rightWidth, usableArea.Height),
            WindowState.SnappedTopLeft =>
                WindowBounds.Create(0, 0, leftWidth, topHeight),
            WindowState.SnappedTopRight =>
                WindowBounds.Create(leftWidth, 0, rightWidth, topHeight),
            WindowState.SnappedBottomLeft =>
                WindowBounds.Create(0, topHeight, leftWidth, bottomHeight),
            WindowState.SnappedBottomRight =>
                WindowBounds.Create(leftWidth, topHeight, rightWidth, bottomHeight),
            _ => null,
        };
    }

    /// <summary>Returns whether the state fixes the window to part of the usable area.</summary>
    public static bool IsSnapped(WindowState state) => state
        is WindowState.SnappedLeft
        or WindowState.SnappedRight
        or WindowState.SnappedTopLeft
        or WindowState.SnappedTopRight
        or WindowState.SnappedBottomLeft
        or WindowState.SnappedBottomRight;

    /// <summary>Returns whether the state replaces the window's own bounds.</summary>
    public static bool OverridesBounds(WindowState state) =>
        IsSnapped(state) || state is WindowState.Maximized or WindowState.FullScreen;
}
