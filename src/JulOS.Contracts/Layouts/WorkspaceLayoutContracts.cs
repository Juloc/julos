namespace JulOS.Contracts.Layouts;

/// <summary>Stable presentation-mode names of a stored layout.</summary>
public static class PresentationModeNames
{
    /// <summary>Overlapping windows the user positions freely.</summary>
    public const string Freeform = "freeform";

    /// <summary>Windows arranged by the Shell without overlap.</summary>
    public const string Tiled = "tiled";

    /// <summary>A phone workspace with no foreground window.</summary>
    public const string PhoneEmpty = "phone-empty";

    /// <summary>A phone workspace showing exactly one foreground window.</summary>
    public const string PhoneSingle = "phone-single";

    /// <summary>A phone workspace showing two foreground windows.</summary>
    public const string PhoneSplit = "phone-split";

    /// <summary>Every presentation mode, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } = [Freeform, Tiled, PhoneEmpty, PhoneSingle, PhoneSplit];
}

/// <summary>Stable layout-scope names.</summary>
public static class LayoutScopeNames
{
    /// <summary>One layout per workspace class, shared by every device of the user.</summary>
    public const string Shared = "shared";

    /// <summary>A layout private to one registered client device.</summary>
    public const string Device = "device";
}

/// <summary>Stable restore-mode names.</summary>
public static class RestoreModeNames
{
    /// <summary>Restore the persisted windows.</summary>
    public const string Resume = "resume";

    /// <summary>Start with no restored windows and persist nothing.</summary>
    public const string Fresh = "fresh";
}

/// <summary>Stable workspace-layout error codes.</summary>
public static class WorkspaceLayoutErrorCodes
{
    /// <summary>The submitted layout violates geometry, identity or presentation rules.</summary>
    public const string Invalid = "desktop.layout_invalid";

    /// <summary>The resolved workspace runs in fresh mode, which never persists.</summary>
    public const string PersistenceDisabled = "desktop.layout_persistence_disabled";

    /// <summary>The workspace class in the route is not a workspace class.</summary>
    public const string WorkspaceClassInvalid = "desktop.workspace_class_invalid";

    /// <summary>A phone was asked to show more foreground windows than it has.</summary>
    public const string PhoneForegroundLimitExceeded = "desktop.phone_foreground_limit_exceeded";
}

/// <summary>One persisted desktop window.</summary>
/// <param name="WindowId">Stable window identity.</param>
/// <param name="ApplicationDefinitionId">The application the window shows.</param>
/// <param name="LaunchTargetId">The target it was opened against, when there is one.</param>
/// <param name="State">Window state name.</param>
/// <param name="X">Current left edge.</param>
/// <param name="Y">Current top edge.</param>
/// <param name="Width">Current width.</param>
/// <param name="Height">Current height.</param>
/// <param name="RestoreX">Left edge to return to.</param>
/// <param name="RestoreY">Top edge to return to.</param>
/// <param name="RestoreWidth">Width to return to.</param>
/// <param name="RestoreHeight">Height to return to.</param>
/// <param name="ZIndex">Stacking position; higher is nearer the front.</param>
/// <param name="SessionReferenceId">Runtime session this window references, if any.</param>
/// <param name="DisplaySlot">Zero-based logical display position.</param>
/// <remarks>
/// The window carries no workspace class of its own. The layout it is written into decides
/// that, so a client cannot move a window between workspace classes through request data.
/// </remarks>
public sealed record DesktopWindowContract(
    Guid WindowId,
    Guid ApplicationDefinitionId,
    Guid? LaunchTargetId,
    string State,
    int X,
    int Y,
    int Width,
    int Height,
    int RestoreX,
    int RestoreY,
    int RestoreWidth,
    int RestoreHeight,
    int ZIndex,
    Guid? SessionReferenceId,
    int DisplaySlot);

/// <summary>One persisted widget placement.</summary>
/// <param name="WidgetPlacementId">Stable placement identity.</param>
/// <param name="WidgetKey">Which widget is placed.</param>
/// <param name="GridColumn">Zero-based grid column.</param>
/// <param name="GridRow">Zero-based grid row.</param>
/// <param name="WidthUnits">Width in grid units.</param>
/// <param name="HeightUnits">Height in grid units.</param>
public sealed record WidgetPlacementContract(
    Guid WidgetPlacementId,
    string WidgetKey,
    int GridColumn,
    int GridRow,
    int WidthUnits,
    int HeightUnits);

/// <summary>The arrangement of one stored layout.</summary>
/// <param name="LayoutId">Stored identity, or null for a transient fresh-mode layout.</param>
/// <param name="Name">Layout name.</param>
/// <param name="PresentationMode">How the windows are arranged.</param>
/// <param name="PrimaryWindowId">Phone foreground window, when the mode has one.</param>
/// <param name="SecondaryWindowId">Second phone foreground window, only in a split.</param>
/// <param name="SplitRatioPermille">Split position in permille, only in a split.</param>
/// <param name="DisplayCount">How many display participants the layout is arranged for.</param>
/// <param name="UpdatedAtUtc">When the layout was last written.</param>
/// <param name="Windows">The windows, ordered from back to front.</param>
/// <param name="Widgets">The desktop widget placements.</param>
public sealed record WorkspaceLayoutDocument(
    Guid? LayoutId,
    string Name,
    string PresentationMode,
    Guid? PrimaryWindowId,
    Guid? SecondaryWindowId,
    int? SplitRatioPermille,
    int DisplayCount,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DesktopWindowContract> Windows,
    IReadOnlyList<WidgetPlacementContract> Widgets);

/// <summary>The layout a session resolved for one workspace class, and how it resolved.</summary>
/// <param name="WorkspaceClass">The workspace class the layout belongs to.</param>
/// <param name="LayoutScope">Whether the shared or the device layout was selected.</param>
/// <param name="RestoreMode">Whether windows are restored or the workspace starts empty.</param>
/// <param name="PersistenceEnabled">False in fresh mode, where no write is accepted.</param>
/// <param name="Revision">Concurrency revision; zero for a transient fresh-mode layout.</param>
/// <param name="Layout">The resolved arrangement.</param>
public sealed record WorkspaceLayoutResponse(
    string WorkspaceClass,
    string LayoutScope,
    string RestoreMode,
    bool PersistenceEnabled,
    int Revision,
    WorkspaceLayoutDocument Layout);

/// <summary>Replaces the resolved layout of one workspace class using optimistic concurrency.</summary>
/// <param name="Layout">Complete replacement arrangement.</param>
/// <param name="ExpectedRevision">The revision the caller based the write on; zero creates.</param>
public sealed record WorkspaceLayoutWriteRequest(
    WorkspaceLayoutDocument Layout,
    int ExpectedRevision);

/// <summary>The realtime event raised when a stored layout changes.</summary>
public static class WorkspaceLayoutEvents
{
    /// <summary>A workspace layout was created or replaced.</summary>
    public const string Changed = "workspace_layout.changed";
}
