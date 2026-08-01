using JulOS.Domain.Primitives;

namespace JulOS.Domain.Layouts;

/// <summary>
/// The stored desktop of one user in one viewport class.
/// </summary>
/// <remarks>
/// A layout belongs to exactly one viewport class. A phone and a desktop keep separate
/// layouts, so arranging windows on a wide screen never overwrites what the same user
/// set up on a narrow one.
/// </remarks>
public sealed class DesktopLayout
{
    private readonly List<DesktopWindow> windows = [];

    private readonly List<WidgetPlacement> widgets = [];

    private DesktopLayout(DesktopLayoutId id, ViewportClass viewportClass)
    {
        this.Id = id;
        this.ViewportClass = viewportClass;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this layout.</summary>
    public DesktopLayoutId Id { get; }

    /// <summary>The viewport class this layout applies to.</summary>
    public ViewportClass ViewportClass { get; }

    /// <summary>The windows in the layout, ordered from back to front.</summary>
    public IReadOnlyList<DesktopWindow> Windows => this.windows;

    /// <summary>The widgets placed on the desktop.</summary>
    public IReadOnlyList<WidgetPlacement> Widgets => this.widgets;

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Creates an empty layout for one viewport class.</summary>
    public static DesktopLayout Create(DesktopLayoutId id, ViewportClass viewportClass) =>
        new(id, viewportClass);

    /// <summary>
    /// Adds a window at the front of the stack.
    /// </summary>
    /// <exception cref="DomainRuleViolationException">A window with that identity is already open.</exception>
    public void AddWindow(DesktopWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (this.windows.Exists(existing => existing.Id == window.Id))
        {
            throw new DomainRuleViolationException(
                "layout.window.already_open",
                "A window identity can appear in a layout only once.");
        }

        this.windows.Add(window);
        this.NormalizeZOrder();
        this.Revision = this.Revision.Next();
    }

    /// <summary>Removes a window from the layout.</summary>
    /// <exception cref="DomainRuleViolationException">No such window is open.</exception>
    public void RemoveWindow(WindowId windowId)
    {
        var window = this.RequireWindow(windowId);

        this.windows.Remove(window);
        this.NormalizeZOrder();
        this.Revision = this.Revision.Next();
    }

    /// <summary>Raises a window to the front of the stack.</summary>
    /// <exception cref="DomainRuleViolationException">No such window is open.</exception>
    public void Focus(WindowId windowId)
    {
        var window = this.RequireWindow(windowId);

        this.windows.Remove(window);
        this.windows.Add(window);
        this.NormalizeZOrder();
        this.Revision = this.Revision.Next();
    }

    /// <summary>Returns the window that is nearest the front, or <see langword="null"/> when none is open.</summary>
    public DesktopWindow? FrontWindow => this.windows.Count == 0 ? null : this.windows[^1];

    /// <summary>Adds a widget to the desktop grid.</summary>
    /// <exception cref="DomainRuleViolationException">The placement overlaps a widget that is already there.</exception>
    public void AddWidget(WidgetPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        var overlapping = this.widgets.Find(placement.Overlaps);

        if (overlapping is not null)
        {
            throw new DomainRuleViolationException(
                "layout.widget.overlaps",
                "Two widgets cannot occupy the same grid cells; one would hide the other.");
        }

        this.widgets.Add(placement);
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Renumbers the stack so that z-order is a gap-free sequence with no duplicates.
    /// </summary>
    /// <remarks>
    /// Stored layouts arrive from clients and from earlier versions, where two windows can
    /// carry the same z-index. Rendering would then pick an arbitrary winner and a click
    /// could land on the wrong window, so the list order is authoritative and the indices
    /// are derived from it.
    /// </remarks>
    private void NormalizeZOrder()
    {
        for (var index = 0; index < this.windows.Count; index++)
        {
            this.windows[index].ZIndex = index;
        }
    }

    private DesktopWindow RequireWindow(WindowId windowId)
    {
        return this.windows.Find(existing => existing.Id == windowId)
            ?? throw new DomainRuleViolationException(
                "layout.window.not_open",
                "The layout contains no window with that identity.");
    }
}
