namespace JulOS.Domain.Applications;

/// <summary>
/// The default and minimum window size an application declares.
/// </summary>
/// <remarks>
/// The window manager clamps a restored window to the minimum, so an application that
/// becomes unusable below a certain size can say so once instead of every caller
/// guessing.
/// </remarks>
public readonly record struct WindowSizeConstraints
{
    private const int SmallestUsableEdge = 120;

    private const int LargestReasonableEdge = 16384;

    private WindowSizeConstraints(int defaultWidth, int defaultHeight, int minimumWidth, int minimumHeight)
    {
        this.DefaultWidth = defaultWidth;
        this.DefaultHeight = defaultHeight;
        this.MinimumWidth = minimumWidth;
        this.MinimumHeight = minimumHeight;
    }

    /// <summary>The width a new window opens at, in logical pixels.</summary>
    public int DefaultWidth { get; }

    /// <summary>The height a new window opens at, in logical pixels.</summary>
    public int DefaultHeight { get; }

    /// <summary>The width below which the application is unusable, in logical pixels.</summary>
    public int MinimumWidth { get; }

    /// <summary>The height below which the application is unusable, in logical pixels.</summary>
    public int MinimumHeight { get; }

    /// <summary>Reads a declared set of window size constraints.</summary>
    /// <exception cref="DomainRuleViolationException">
    /// An edge is outside the usable range, or a default is smaller than its minimum.
    /// </exception>
    public static WindowSizeConstraints Create(int defaultWidth, int defaultHeight, int minimumWidth, int minimumHeight)
    {
        EnsureUsable(defaultWidth, nameof(defaultWidth));
        EnsureUsable(defaultHeight, nameof(defaultHeight));
        EnsureUsable(minimumWidth, nameof(minimumWidth));
        EnsureUsable(minimumHeight, nameof(minimumHeight));

        if (defaultWidth < minimumWidth || defaultHeight < minimumHeight)
        {
            throw new DomainRuleViolationException(
                "application.window_size.default_below_minimum",
                "A window cannot open smaller than the size the application declares as usable.");
        }

        return new WindowSizeConstraints(defaultWidth, defaultHeight, minimumWidth, minimumHeight);
    }

    private static void EnsureUsable(int edge, string name)
    {
        if (edge is < SmallestUsableEdge or > LargestReasonableEdge)
        {
            throw new DomainRuleViolationException(
                "application.window_size.out_of_range",
                $"'{name}' must be between {SmallestUsableEdge} and {LargestReasonableEdge} logical pixels.");
        }
    }
}
