namespace JulOS.Contracts.Layouts;

/// <summary>Stable viewport names used to isolate desktop layouts.</summary>
public static class DesktopViewportNames
{
    public const string Desktop = "desktop";
    public const string Tablet = "tablet";
    public const string Mobile = "mobile";
}

/// <summary>One persisted desktop window.</summary>
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
    Guid? SessionReferenceId);

/// <summary>One persisted widget placement.</summary>
public sealed record WidgetPlacementContract(
    Guid WidgetPlacementId,
    string WidgetKey,
    int GridColumn,
    int GridRow,
    int WidthUnits,
    int HeightUnits);

/// <summary>The default layout for one user and viewport class.</summary>
public sealed record DesktopLayoutResponse(
    Guid LayoutId,
    string Viewport,
    string Name,
    int Revision,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<DesktopWindowContract> Windows,
    IReadOnlyList<WidgetPlacementContract> Widgets);

/// <summary>Replaces the complete default layout for one viewport using optimistic concurrency.</summary>
public sealed record SaveDesktopLayoutRequest(
    int Revision,
    IReadOnlyList<DesktopWindowContract> Windows,
    IReadOnlyList<WidgetPlacementContract> Widgets);
