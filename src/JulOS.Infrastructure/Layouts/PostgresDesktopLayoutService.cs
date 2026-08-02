using JulOS.Application.Concurrency;
using JulOS.Application.Layouts;
using JulOS.Contracts.Layouts;
using JulOS.Domain.Layouts;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Layouts;

/// <summary>Stores one optimistic-concurrency layout document per user and viewport.</summary>
internal sealed class PostgresDesktopLayoutService : IDesktopLayoutService
{
    private const int MaximumWindows = 100;
    private const int MaximumWidgets = 200;
    private readonly CoreDbContext context;
    private readonly TimeProvider timeProvider;

    public PostgresDesktopLayoutService(CoreDbContext context, TimeProvider timeProvider)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<DesktopLayoutResponse> ReadAsync(
        Guid userId,
        string viewport,
        CancellationToken cancellationToken = default)
    {
        var viewportClass = ParseViewport(userId, viewport);
        var row = await this.context.DesktopLayouts
            .AsNoTracking()
            .Include(layout => layout.Windows)
            .Include(layout => layout.Widgets)
            .SingleOrDefaultAsync(
                layout => layout.UserId == userId
                    && layout.ViewportClass == viewportClass
                    && layout.IsDefault,
                cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? new DesktopLayoutResponse(
                Guid.Empty,
                ViewportName(viewportClass),
                "Default",
                Revision: 0,
                UpdatedAtUtc: DateTimeOffset.MinValue,
                Windows: [],
                Widgets: [])
            : ToResponse(row);
    }

    public async Task<DesktopLayoutResponse> SaveAsync(
        Guid userId,
        string viewport,
        SaveDesktopLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewportClass = ParseViewport(userId, viewport);
        Validate(request);

        var row = await this.context.DesktopLayouts
            .Include(layout => layout.Windows)
            .Include(layout => layout.Widgets)
            .SingleOrDefaultAsync(
                layout => layout.UserId == userId
                    && layout.ViewportClass == viewportClass
                    && layout.IsDefault,
                cancellationToken)
            .ConfigureAwait(false);
        var now = this.timeProvider.GetUtcNow();

        if (row is null)
        {
            if (request.Revision != 0)
            {
                throw new ConcurrencyConflictException(
                    currentRevision: 0,
                    new InvalidOperationException("The viewport layout does not exist yet."));
            }

            row = new DesktopLayoutRow
            {
                Id = Guid.CreateVersion7(now),
                UserId = userId,
                ViewportClass = viewportClass,
                Name = "Default",
                IsDefault = true,
                Revision = 1,
                UpdatedAtUtc = now,
            };
            this.context.DesktopLayouts.Add(row);
        }
        else
        {
            if (row.Revision != request.Revision)
            {
                throw new ConcurrencyConflictException(
                    row.Revision,
                    new InvalidOperationException("The desktop layout changed concurrently."));
            }

            this.context.DesktopWindows.RemoveRange(row.Windows);
            this.context.WidgetPlacements.RemoveRange(row.Widgets);
            row.Windows.Clear();
            row.Widgets.Clear();
            row.Revision = checked(row.Revision + 1);
            row.UpdatedAtUtc = now;
        }

        foreach (var window in request.Windows.OrderBy(window => window.ZIndex))
        {
            row.Windows.Add(ToRow(row.Id, window, now));
        }
        foreach (var widget in request.Widgets)
        {
            row.Widgets.Add(ToRow(row.Id, widget));
        }

        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToResponse(row);
    }

    private static DesktopWindowRow ToRow(
        Guid layoutId,
        DesktopWindowContract window,
        DateTimeOffset now) => new()
    {
        Id = window.WindowId,
        DesktopLayoutId = layoutId,
        ApplicationDefinitionId = window.ApplicationDefinitionId,
        LaunchTargetId = window.LaunchTargetId,
        State = ParseWindowState(window.State),
        X = window.X,
        Y = window.Y,
        Width = window.Width,
        Height = window.Height,
        RestoreX = window.RestoreX,
        RestoreY = window.RestoreY,
        RestoreWidth = window.RestoreWidth,
        RestoreHeight = window.RestoreHeight,
        ZIndex = window.ZIndex,
        SessionReferenceId = window.SessionReferenceId,
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        Revision = 1,
    };

    private static WidgetPlacementRow ToRow(Guid layoutId, WidgetPlacementContract widget) => new()
    {
        Id = widget.WidgetPlacementId,
        DesktopLayoutId = layoutId,
        WidgetKey = widget.WidgetKey,
        Column = widget.GridColumn,
        Row = widget.GridRow,
        WidthUnits = widget.WidthUnits,
        HeightUnits = widget.HeightUnits,
        Revision = 1,
    };

    private static DesktopLayoutResponse ToResponse(DesktopLayoutRow row) => new(
        row.Id,
        ViewportName(row.ViewportClass),
        row.Name,
        row.Revision,
        row.UpdatedAtUtc,
        row.Windows
            .OrderBy(window => window.ZIndex)
            .Select(window => new DesktopWindowContract(
                window.Id,
                window.ApplicationDefinitionId,
                window.LaunchTargetId,
                WindowStateName(window.State),
                window.X,
                window.Y,
                window.Width,
                window.Height,
                window.RestoreX,
                window.RestoreY,
                window.RestoreWidth,
                window.RestoreHeight,
                window.ZIndex,
                window.SessionReferenceId))
            .ToArray(),
        row.Widgets
            .OrderBy(widget => widget.Row)
            .ThenBy(widget => widget.Column)
            .Select(widget => new WidgetPlacementContract(
                widget.Id,
                widget.WidgetKey,
                widget.Column,
                widget.Row,
                widget.WidthUnits,
                widget.HeightUnits))
            .ToArray());

    private static void Validate(SaveDesktopLayoutRequest request)
    {
        if (request.Revision < 0
            || request.Windows.Count > MaximumWindows
            || request.Widgets.Count > MaximumWidgets)
        {
            throw new ArgumentException("The desktop layout document is invalid.", nameof(request));
        }

        var windowIds = new HashSet<Guid>();
        var zIndexes = new HashSet<int>();
        foreach (var window in request.Windows)
        {
            if (window.WindowId == Guid.Empty
                || window.ApplicationDefinitionId == Guid.Empty
                || window.LaunchTargetId == Guid.Empty
                || window.SessionReferenceId == Guid.Empty
                || window.ZIndex < 0
                || !windowIds.Add(window.WindowId)
                || !zIndexes.Add(window.ZIndex)
                || window.Width is < 1 or > 16384
                || window.Height is < 1 or > 16384
                || window.RestoreWidth is < 1 or > 16384
                || window.RestoreHeight is < 1 or > 16384
                || Math.Abs((long)window.X) > 65536
                || Math.Abs((long)window.Y) > 65536
                || Math.Abs((long)window.RestoreX) > 65536
                || Math.Abs((long)window.RestoreY) > 65536)
            {
                throw new ArgumentException("A desktop window is invalid.", nameof(request));
            }
            _ = ParseWindowState(window.State);
        }

        if (!zIndexes.Order().SequenceEqual(Enumerable.Range(0, zIndexes.Count)))
        {
            throw new ArgumentException("Desktop window z-indexes must be gap-free.", nameof(request));
        }

        var widgetIds = new HashSet<Guid>();
        foreach (var widget in request.Widgets)
        {
            if (widget.WidgetPlacementId == Guid.Empty
                || string.IsNullOrWhiteSpace(widget.WidgetKey)
                || widget.WidgetKey.Length > 256
                || !widgetIds.Add(widget.WidgetPlacementId)
                || widget.GridColumn < 0
                || widget.GridRow < 0
                || widget.WidthUnits < 1
                || widget.HeightUnits < 1)
            {
                throw new ArgumentException("A widget placement is invalid.", nameof(request));
            }
        }
    }

    private static ViewportClass ParseViewport(Guid userId, string viewport)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(viewport))
        {
            throw new ArgumentException("The desktop layout identity is invalid.");
        }

        var enumName = viewport switch
        {
            DesktopViewportNames.Desktop => "Desktop",
            DesktopViewportNames.Tablet => "Tablet",
            DesktopViewportNames.Mobile => "Mobile",
            _ => throw new ArgumentException("The desktop viewport is invalid.", nameof(viewport)),
        };
        return Enum.Parse<ViewportClass>(enumName, ignoreCase: true);
    }

    private static string ViewportName(ViewportClass viewport) => viewport.ToString() switch
    {
        "Desktop" => DesktopViewportNames.Desktop,
        "Tablet" => DesktopViewportNames.Tablet,
        "Mobile" => DesktopViewportNames.Mobile,
        _ => throw new InvalidOperationException("Unknown desktop viewport class."),
    };

    private static WindowState ParseWindowState(string state) =>
        Enum.TryParse<WindowState>(state, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException("The desktop window state is invalid.", nameof(state));

    private static string WindowStateName(WindowState state) => state.ToString().ToLowerInvariant();
}
