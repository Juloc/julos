namespace JulOS.Domain.Layouts;

/// <summary>
/// The position and size of a window, in logical pixels.
/// </summary>
/// <remarks>
/// Coordinates may be negative because a window can hang off the left or top edge while
/// being dragged. A non-positive size cannot: a window with no area is not reachable and
/// could never be dragged back.
/// </remarks>
public readonly record struct WindowBounds
{
    private const int SmallestUsableEdge = 1;

    private const int LargestReasonableEdge = 16384;

    private const int FurthestReasonableOffset = 65536;

    private WindowBounds(int x, int y, int width, int height)
    {
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>The distance from the left edge of the usable area.</summary>
    public int X { get; }

    /// <summary>The distance from the top edge of the usable area.</summary>
    public int Y { get; }

    /// <summary>The window width.</summary>
    public int Width { get; }

    /// <summary>The window height.</summary>
    public int Height { get; }

    /// <summary>The distance from the left edge to the right edge of the window.</summary>
    public int Right => this.X + this.Width;

    /// <summary>The distance from the top edge to the bottom edge of the window.</summary>
    public int Bottom => this.Y + this.Height;

    /// <summary>Reads a set of window bounds.</summary>
    /// <exception cref="DomainRuleViolationException">The bounds describe a window that cannot be used.</exception>
    public static WindowBounds Create(int x, int y, int width, int height)
    {
        if (width < SmallestUsableEdge || height < SmallestUsableEdge)
        {
            throw new DomainRuleViolationException(
                "layout.bounds.not_positive",
                "A window with no area cannot be seen or dragged back into view.");
        }

        if (width > LargestReasonableEdge || height > LargestReasonableEdge)
        {
            throw new DomainRuleViolationException(
                "layout.bounds.too_large",
                $"A window edge cannot exceed {LargestReasonableEdge} logical pixels.");
        }

        if (Math.Abs(x) > FurthestReasonableOffset || Math.Abs(y) > FurthestReasonableOffset)
        {
            throw new DomainRuleViolationException(
                "layout.bounds.out_of_range",
                $"A window origin cannot be further than {FurthestReasonableOffset} logical pixels from the usable area.");
        }

        return new WindowBounds(x, y, width, height);
    }

    /// <summary>
    /// Returns bounds moved back until the title bar is reachable inside <paramref name="usableArea"/>.
    /// </summary>
    /// <remarks>
    /// A window whose title bar sits entirely outside the usable area can never be dragged
    /// again, so a restored layout is clamped rather than trusted. Only the origin moves;
    /// the size the user chose is preserved.
    /// </remarks>
    /// <param name="usableArea">The desktop area excluding the taskbar.</param>
    /// <param name="titleBarHeight">The height of the grab area at the top of the window.</param>
    public WindowBounds ClampToReachable(UsableArea usableArea, int titleBarHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(titleBarHeight);

        // At least one title-bar-sized corner has to stay inside the usable area.
        var minimumVisibleWidth = Math.Min(this.Width, titleBarHeight);

        var x = Math.Clamp(this.X, usableArea.Width == 0 ? 0 : minimumVisibleWidth - this.Width, Math.Max(0, usableArea.Width - minimumVisibleWidth));
        var y = Math.Clamp(this.Y, 0, Math.Max(0, usableArea.Height - titleBarHeight));

        return new WindowBounds(x, y, this.Width, this.Height);
    }

    /// <summary>Returns bounds grown to at least the given size.</summary>
    public WindowBounds AtLeast(int minimumWidth, int minimumHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumHeight);

        return new WindowBounds(
            this.X,
            this.Y,
            Math.Max(this.Width, minimumWidth),
            Math.Max(this.Height, minimumHeight));
    }
}
