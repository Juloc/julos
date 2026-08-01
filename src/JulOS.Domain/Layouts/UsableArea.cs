namespace JulOS.Domain.Layouts;

/// <summary>
/// The desktop area a window may occupy, excluding the taskbar.
/// </summary>
/// <remarks>
/// Snapping and clamping are computed against this rather than the viewport, so a snapped
/// window never ends up underneath the taskbar.
/// </remarks>
public readonly record struct UsableArea
{
    private const int LargestReasonableEdge = 16384;

    private UsableArea(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    /// <summary>The usable width in logical pixels.</summary>
    public int Width { get; }

    /// <summary>The usable height in logical pixels.</summary>
    public int Height { get; }

    /// <summary>Reads a usable desktop area.</summary>
    /// <exception cref="DomainRuleViolationException">The area cannot hold a window.</exception>
    public static UsableArea Create(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > LargestReasonableEdge || height > LargestReasonableEdge)
        {
            throw new DomainRuleViolationException(
                "layout.usable_area.invalid",
                $"A usable desktop area is between 1 and {LargestReasonableEdge} logical pixels on each edge.");
        }

        return new UsableArea(width, height);
    }
}
