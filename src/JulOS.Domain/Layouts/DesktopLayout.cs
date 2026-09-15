using JulOS.Domain.Primitives;

namespace JulOS.Domain.Layouts;

/// <summary>
/// The stored desktop of one user in one workspace class.
/// </summary>
/// <remarks>
/// <para>
/// A layout belongs to exactly one workspace class, and that class is immutable: each
/// class is a different layout identity, not a different rendering of one layout. A phone
/// and a desktop therefore keep separate layouts, so arranging windows on a wide screen
/// never overwrites what the same user set up on a narrow one.
/// </para>
/// <para>
/// A layout is either shared by the user or bound to one registered client device. The
/// binding is the <see cref="ClientDeviceId"/>: null means the shared layout of that
/// workspace class. Which of the two a session loads is a resolution decision made from
/// the device's stored preference, never something the layout itself knows.
/// </para>
/// </remarks>
public sealed class DesktopLayout
{
    private readonly List<DesktopWindow> windows = [];

    private readonly List<WidgetPlacement> widgets = [];

    private DesktopLayout(
        DesktopLayoutId id,
        WorkspaceClass workspaceClass,
        Guid? clientDeviceId,
        int displayCount)
    {
        this.Id = id;
        this.WorkspaceClass = workspaceClass;
        this.ClientDeviceId = clientDeviceId;
        this.DisplayCount = displayCount;
        this.PresentationMode = workspaceClass == WorkspaceClass.Phone
            ? PresentationMode.PhoneEmpty
            : PresentationMode.Freeform;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this layout.</summary>
    public DesktopLayoutId Id { get; }

    /// <summary>The workspace class this layout applies to. Immutable.</summary>
    public WorkspaceClass WorkspaceClass { get; }

    /// <summary>The device this layout is private to, or null for the user's shared layout.</summary>
    public Guid? ClientDeviceId { get; }

    /// <summary>Whether this is the shared or a device-scoped layout.</summary>
    public LayoutScope Scope => this.ClientDeviceId is null ? LayoutScope.Shared : LayoutScope.Device;

    /// <summary>How the layout arranges its windows.</summary>
    public PresentationMode PresentationMode { get; private set; }

    /// <summary>The phone foreground window, when the mode has one.</summary>
    public WindowId? PrimaryWindowId { get; private set; }

    /// <summary>The second phone foreground window, only in <see cref="PresentationMode.PhoneSplit"/>.</summary>
    public WindowId? SecondaryWindowId { get; private set; }

    /// <summary>The split position in permille, only in <see cref="PresentationMode.PhoneSplit"/>.</summary>
    public int? SplitRatioPermille { get; private set; }

    /// <summary>How many display participants this layout was arranged for.</summary>
    public int DisplayCount { get; private set; }

    /// <summary>The windows in the layout, ordered from back to front.</summary>
    public IReadOnlyList<DesktopWindow> Windows => this.windows;

    /// <summary>The widgets placed on the desktop.</summary>
    public IReadOnlyList<WidgetPlacement> Widgets => this.widgets;

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>The lowest accepted phone split position, in permille.</summary>
    public const int MinimumSplitRatioPermille = 250;

    /// <summary>The highest accepted phone split position, in permille.</summary>
    public const int MaximumSplitRatioPermille = 750;

    /// <summary>Creates an empty shared layout for one workspace class.</summary>
    public static DesktopLayout CreateShared(DesktopLayoutId id, WorkspaceClass workspaceClass) =>
        new(id, workspaceClass, clientDeviceId: null, DefaultDisplayCount(workspaceClass));

    /// <summary>Creates an empty layout private to one registered client device.</summary>
    /// <exception cref="DomainRuleViolationException">The device identity is empty.</exception>
    public static DesktopLayout CreateForDevice(
        DesktopLayoutId id,
        WorkspaceClass workspaceClass,
        Guid clientDeviceId)
    {
        if (clientDeviceId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "desktop.layout_invalid",
                "A device-scoped layout names the device it belongs to.");
        }

        return new DesktopLayout(id, workspaceClass, clientDeviceId, DefaultDisplayCount(workspaceClass));
    }

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

        if (window.WorkspaceClass != this.WorkspaceClass)
        {
            throw new DomainRuleViolationException(
                "desktop.workspace_class_invalid",
                "A window carries the workspace class of the layout that holds it.");
        }

        if (this.WorkspaceClass != WorkspaceClass.DesktopMulti && window.DisplaySlot != 0)
        {
            throw new DomainRuleViolationException(
                "desktop.workspace_class_invalid",
                "Only a multi-display workspace has display slots other than zero.");
        }

        this.windows.Add(window);
        this.NormalizeZOrder();
        this.Revision = this.Revision.Next();
    }

    /// <summary>Removes a window from the layout.</summary>
    /// <remarks>
    /// Removing a phone foreground window also leaves the presentation mode consistent:
    /// a split whose second window is gone becomes single, and a single whose window is
    /// gone becomes empty. Leaving the identifier behind would point the mode at a window
    /// that no longer exists.
    /// </remarks>
    /// <exception cref="DomainRuleViolationException">No such window is open.</exception>
    public void RemoveWindow(WindowId windowId)
    {
        var window = this.RequireWindow(windowId);

        this.windows.Remove(window);
        this.NormalizeZOrder();
        this.DropForegroundReference(windowId);
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

    /// <summary>Arranges a non-phone workspace freely or without overlap.</summary>
    /// <exception cref="DomainRuleViolationException">The workspace is a phone.</exception>
    public void Arrange(PresentationMode mode)
    {
        if (this.WorkspaceClass == WorkspaceClass.Phone)
        {
            throw new DomainRuleViolationException(
                "desktop.workspace_class_invalid",
                "A phone workspace uses the phone presentation modes.");
        }

        if (mode is not (PresentationMode.Freeform or PresentationMode.Tiled))
        {
            throw new DomainRuleViolationException(
                "desktop.workspace_class_invalid",
                "Only a phone workspace uses the phone presentation modes.");
        }

        this.PresentationMode = mode;
        this.PrimaryWindowId = null;
        this.SecondaryWindowId = null;
        this.SplitRatioPermille = null;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Shows no foreground window on a phone.</summary>
    public void ShowNothing()
    {
        this.RequirePhone();

        this.PresentationMode = PresentationMode.PhoneEmpty;
        this.PrimaryWindowId = null;
        this.SecondaryWindowId = null;
        this.SplitRatioPermille = null;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Shows exactly one foreground window on a phone.</summary>
    /// <exception cref="DomainRuleViolationException">The window is not in this layout.</exception>
    public void ShowSingle(WindowId primary)
    {
        this.RequirePhone();
        _ = this.RequireWindow(primary);

        this.PresentationMode = PresentationMode.PhoneSingle;
        this.PrimaryWindowId = primary;
        this.SecondaryWindowId = null;
        this.SplitRatioPermille = null;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Shows two foreground windows above each other on a phone.</summary>
    /// <exception cref="DomainRuleViolationException">
    /// A window is not in this layout, the two windows are the same, or the ratio is outside
    /// the accepted range.
    /// </exception>
    public void ShowSplit(WindowId primary, WindowId secondary, int splitRatioPermille)
    {
        this.RequirePhone();
        _ = this.RequireWindow(primary);
        _ = this.RequireWindow(secondary);

        if (primary == secondary)
        {
            throw new DomainRuleViolationException(
                "desktop.phone_foreground_limit_exceeded",
                "A phone split shows two different windows.");
        }

        if (splitRatioPermille is < MinimumSplitRatioPermille or > MaximumSplitRatioPermille)
        {
            throw new DomainRuleViolationException(
                "desktop.layout_invalid",
                $"A phone split position is between {MinimumSplitRatioPermille} and {MaximumSplitRatioPermille} permille.");
        }

        this.PresentationMode = PresentationMode.PhoneSplit;
        this.PrimaryWindowId = primary;
        this.SecondaryWindowId = secondary;
        this.SplitRatioPermille = splitRatioPermille;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Records how many display participants the layout is arranged for.</summary>
    /// <exception cref="DomainRuleViolationException">The count is below one, or above one outside multi-display.</exception>
    public void SetDisplayCount(int displayCount)
    {
        if (displayCount < 1)
        {
            throw new DomainRuleViolationException(
                "desktop.layout_invalid",
                "A layout is arranged for at least one display.");
        }

        if (displayCount > 1 && this.WorkspaceClass != WorkspaceClass.DesktopMulti)
        {
            throw new DomainRuleViolationException(
                "desktop.workspace_class_invalid",
                "Only a multi-display workspace spans more than one display.");
        }

        this.DisplayCount = displayCount;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Restores a persisted layout without advancing its revision.</summary>
    public static DesktopLayout Restore(
        DesktopLayoutId id,
        WorkspaceClass workspaceClass,
        Guid? clientDeviceId,
        PresentationMode presentationMode,
        WindowId? primaryWindowId,
        WindowId? secondaryWindowId,
        int? splitRatioPermille,
        int displayCount,
        Revision revision,
        IEnumerable<DesktopWindow> windows,
        IEnumerable<WidgetPlacement> widgets)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(widgets);

        var layout = new DesktopLayout(id, workspaceClass, clientDeviceId, displayCount)
        {
            PresentationMode = presentationMode,
            PrimaryWindowId = primaryWindowId,
            SecondaryWindowId = secondaryWindowId,
            SplitRatioPermille = splitRatioPermille,
            Revision = revision,
        };
        layout.windows.AddRange(windows);
        layout.widgets.AddRange(widgets);
        return layout;
    }

    /// <summary>
    /// Selects the window a migrated phone layout shows in the foreground.
    /// </summary>
    /// <remarks>
    /// The rule is deterministic so that migrating the same stored layout twice produces
    /// the same result: the non-minimized window nearest the front, ties broken by
    /// identity. Split is never produced here, because showing two windows at once is
    /// always an explicit user action and must not be inferred from an old layout that
    /// merely contained several windows.
    /// </remarks>
    public static DesktopWindow? SelectMigratedPrimary(IEnumerable<DesktopWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        return windows
            .Where(window => window.State != WindowState.Minimized)
            .OrderByDescending(window => window.ZIndex)
            .ThenBy(window => window.Id.Value)
            .FirstOrDefault();
    }

    private static int DefaultDisplayCount(WorkspaceClass workspaceClass) =>
        workspaceClass == WorkspaceClass.DesktopMulti ? 2 : 1;

    private void RequirePhone()
    {
        if (this.WorkspaceClass != WorkspaceClass.Phone)
        {
            throw new DomainRuleViolationException(
                "desktop.workspace_class_invalid",
                "Only a phone workspace uses the phone presentation modes.");
        }
    }

    private void DropForegroundReference(WindowId windowId)
    {
        if (this.SecondaryWindowId == windowId)
        {
            this.SecondaryWindowId = null;
            this.SplitRatioPermille = null;
            this.PresentationMode = PresentationMode.PhoneSingle;
            return;
        }

        if (this.PrimaryWindowId != windowId)
        {
            return;
        }

        // The remaining split window becomes the single foreground window rather than
        // vanishing with the one that was closed.
        this.PrimaryWindowId = this.SecondaryWindowId;
        this.SecondaryWindowId = null;
        this.SplitRatioPermille = null;
        this.PresentationMode = this.PrimaryWindowId is null
            ? PresentationMode.PhoneEmpty
            : PresentationMode.PhoneSingle;
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
