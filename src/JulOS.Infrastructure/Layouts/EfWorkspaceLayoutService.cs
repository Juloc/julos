using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Concurrency;
using JulOS.Application.Events;
using JulOS.Application.Layouts;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Layouts;
using JulOS.Domain.Layouts;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Layouts;

/// <summary>
/// Resolves and stores one layout per user, workspace class and layout scope.
/// </summary>
/// <remarks>
/// Every query is filtered by the authenticated user. The device key selects which of that
/// user's devices the preference is read from and nothing else: a key belonging to another
/// user resolves to no device, which falls back to the shared layout rather than revealing
/// or writing anything of theirs.
/// </remarks>
internal sealed class EfWorkspaceLayoutService : IWorkspaceLayoutService
{
    private const int MaximumWindows = 100;
    private const int MaximumWidgets = 200;
    private const string DefaultLayoutName = "Default";

    private readonly CoreDbContext context;
    private readonly IIdentifierGenerator identifiers;
    private readonly TimeProvider timeProvider;
    private readonly IRealtimeEventPublisher? events;

    public EfWorkspaceLayoutService(
        CoreDbContext context,
        IIdentifierGenerator identifiers,
        TimeProvider timeProvider,
        IRealtimeEventPublisher? events = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.identifiers = identifiers ?? throw new ArgumentNullException(nameof(identifiers));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.events = events;
    }

    /// <summary>Announces that a stored layout changed.</summary>
    /// <remarks>
    /// The event carries the layout identity and revision only. A client refetches the
    /// authoritative layout, so no window geometry travels on the event bus and a listener
    /// cannot reconstruct another session's desktop from the notification.
    /// </remarks>
    private async Task PublishChangedAsync(Guid layoutId, int revision, CancellationToken cancellationToken)
    {
        if (this.events is null)
        {
            return;
        }

        await this.events.PublishAsync(
            new RealtimeEventNotification(
                WorkspaceLayoutEvents.Changed,
                layoutId.ToString(),
                layoutId.ToString(),
                revision,
                EmptyPayload),
            cancellationToken).ConfigureAwait(false);
    }

    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement;

    public async Task<WorkspaceLayoutResponse> ReadCurrentAsync(
        Guid userId,
        string workspaceClass,
        string? presentedKey,
        CancellationToken cancellationToken = default)
    {
        var resolved = await this
            .ResolveAsync(userId, workspaceClass, presentedKey, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.RestoreMode == RestoreMode.Fresh)
        {
            // Fresh mode reads nothing and writes nothing. Returning the stored layout here
            // and only refusing the write would still restore windows the user asked not to
            // have restored.
            return TransientResponse(resolved);
        }

        var row = await this.Query(userId, resolved)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? EmptyResponse(resolved) : ToResponse(resolved, row);
    }

    public async Task<WorkspaceLayoutWriteResult> WriteCurrentAsync(
        Guid userId,
        string workspaceClass,
        string? presentedKey,
        WorkspaceLayoutWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolved = await this
            .ResolveAsync(userId, workspaceClass, presentedKey, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.RestoreMode == RestoreMode.Fresh)
        {
            throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.PersistenceDisabled,
                "This workspace is set to start fresh, so its window state is not stored.");
        }

        Validate(request, resolved.WorkspaceClass);

        var row = await this.Query(userId, resolved)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null && request.ExpectedRevision != 0)
        {
            throw new ConcurrencyConflictException(
                currentRevision: 0,
                new InvalidOperationException("The workspace layout does not exist yet."));
        }

        if (row is not null && row.Revision != request.ExpectedRevision)
        {
            throw new ConcurrencyConflictException(
                row.Revision,
                new InvalidOperationException("The workspace layout changed concurrently."));
        }

        var now = this.timeProvider.GetUtcNow();
        var created = row is null;

        await using var transaction = await this.context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new DesktopLayoutRow
            {
                Id = this.identifiers.Create(),
                UserId = userId,
                WorkspaceClass = resolved.WorkspaceClass,
                ClientDeviceId = resolved.ClientDeviceId,
                Name = DefaultLayoutName,
                PresentationMode = NeutralMode(resolved.WorkspaceClass),
                DisplayCount = 1,
                Revision = 1,
                UpdatedAtUtc = now,
            };
            this.context.DesktopLayouts.Add(row);
        }
        else
        {
            // The old child rows are deleted and flushed before the new ones are inserted.
            // A single SaveChanges does not guarantee that the delete of an old
            // (layout, z_index) row runs before the insert of the same z_index, so simply
            // bringing a window to the front would violate ux_desktop_windows_layout_z_index.
            // Clearing the foreground references first is what lets the windows they name be
            // deleted at all.
            row.PresentationMode = NeutralMode(resolved.WorkspaceClass);
            row.PrimaryWindowId = null;
            row.SecondaryWindowId = null;
            row.SplitRatioPermille = null;
            this.context.DesktopWindows.RemoveRange(row.Windows);
            this.context.WidgetPlacements.RemoveRange(row.Widgets);
            row.Windows.Clear();
            row.Widgets.Clear();
            row.Revision = checked(row.Revision + 1);
            row.UpdatedAtUtc = now;
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var ordered = request.Layout.Windows.OrderBy(window => window.ZIndex).ToList();
        for (var zIndex = 0; zIndex < ordered.Count; zIndex++)
        {
            row.Windows.Add(ToRow(row.Id, ordered[zIndex], zIndex, resolved.WorkspaceClass, now));
        }

        foreach (var widget in request.Layout.Widgets)
        {
            row.Widgets.Add(ToRow(row.Id, widget));
        }

        ApplyPresentation(row, request.Layout, resolved.WorkspaceClass);

        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(row.Id, row.Revision, cancellationToken).ConfigureAwait(false);

        return new WorkspaceLayoutWriteResult(ToResponse(resolved, row), created);
    }

    public async Task<WorkspaceLayoutWriteResult> InitializeMultiDisplayAsync(
        Guid userId,
        string? presentedKey,
        bool copyFromDesktopSingle,
        int displayCount,
        CancellationToken cancellationToken = default)
    {
        if (displayCount < 2)
        {
            throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.InvalidLayout,
                "A multi-display layout is arranged for at least two display participants.");
        }

        var target = await this
            .ResolveAsync(userId, WorkspaceClassNames.DesktopMulti, presentedKey, cancellationToken)
            .ConfigureAwait(false);

        var existing = await this.Query(userId, target)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new WorkspaceLayoutWriteResult(ToResponse(target, existing), Created: false);
        }

        var source = copyFromDesktopSingle
            ? await this.Query(userId, target with { WorkspaceClass = WorkspaceClass.DesktopSingle })
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var now = this.timeProvider.GetUtcNow();
        var row = new DesktopLayoutRow
        {
            Id = this.identifiers.Create(),
            UserId = userId,
            WorkspaceClass = WorkspaceClass.DesktopMulti,
            ClientDeviceId = target.ClientDeviceId,
            Name = DefaultLayoutName,
            PresentationMode = PresentationMode.Freeform,
            DisplayCount = displayCount,
            Revision = 1,
            UpdatedAtUtc = now,
        };

        if (source is not null)
        {
            // Every copied window lands on slot zero. No physical topology is invented: the
            // user arranges the additional displays, the migration does not guess them.
            foreach (var window in source.Windows.OrderBy(window => window.ZIndex))
            {
                var copy = Clone(window);
                copy.Id = this.identifiers.Create();
                copy.DesktopLayoutId = row.Id;
                copy.WorkspaceClass = WorkspaceClass.DesktopMulti;
                copy.DisplaySlot = 0;
                copy.CreatedAtUtc = now;
                copy.UpdatedAtUtc = now;
                row.Windows.Add(copy);
            }

            foreach (var widget in source.Widgets)
            {
                row.Widgets.Add(new WidgetPlacementRow
                {
                    Id = this.identifiers.Create(),
                    DesktopLayoutId = row.Id,
                    WidgetKey = widget.WidgetKey,
                    Column = widget.Column,
                    Row = widget.Row,
                    WidthUnits = widget.WidthUnits,
                    HeightUnits = widget.HeightUnits,
                    Revision = 1,
                });
            }
        }

        this.context.DesktopLayouts.Add(row);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(row.Id, row.Revision, cancellationToken).ConfigureAwait(false);
        return new WorkspaceLayoutWriteResult(ToResponse(target, row), Created: true);
    }

    /// <summary>The layout identity a request resolves to, before any row is read.</summary>
    private readonly record struct ResolvedWorkspace(
        WorkspaceClass WorkspaceClass,
        LayoutScope Scope,
        RestoreMode RestoreMode,
        Guid? ClientDeviceId);

    private async Task<ResolvedWorkspace> ResolveAsync(
        Guid userId,
        string workspaceClass,
        string? presentedKey,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.InvalidLayout,
                "The request is not associated with an authenticated user.");
        }

        var parsed = ParseWorkspaceClass(workspaceClass);
        if (presentedKey is null)
        {
            return new ResolvedWorkspace(parsed, LayoutScope.Shared, RestoreMode.Resume, null);
        }

        var hash = HashKey(presentedKey);
        var device = await this.context.ClientDevices
            .AsNoTracking()
            .Include(row => row.Preferences)
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == userId && row.ClientInstanceKeyHash == hash,
                cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            // An unknown or foreign key is not an error: the session simply has no device
            // preference yet and gets the documented shared/resume default.
            return new ResolvedWorkspace(parsed, LayoutScope.Shared, RestoreMode.Resume, null);
        }

        var preference = device.Preferences.SingleOrDefault(entry => entry.WorkspaceClass == parsed);
        var scope = preference?.LayoutScope ?? LayoutScope.Shared;
        var restoreMode = preference?.RestoreMode ?? RestoreMode.Resume;

        return new ResolvedWorkspace(
            parsed,
            scope,
            restoreMode,
            scope == LayoutScope.Device ? device.Id : null);
    }

    private IQueryable<DesktopLayoutRow> Query(Guid userId, ResolvedWorkspace resolved) =>
        this.context.DesktopLayouts
            .Include(layout => layout.Windows)
            .Include(layout => layout.Widgets)
            .Where(layout => layout.UserId == userId
                && layout.WorkspaceClass == resolved.WorkspaceClass
                && layout.ClientDeviceId == resolved.ClientDeviceId);

    private static void ApplyPresentation(
        DesktopLayoutRow row,
        WorkspaceLayoutDocument document,
        WorkspaceClass workspaceClass)
    {
        var mode = ParsePresentationMode(document.PresentationMode);
        var isPhoneMode = mode is PresentationMode.PhoneEmpty
            or PresentationMode.PhoneSingle
            or PresentationMode.PhoneSplit;

        if (isPhoneMode != (workspaceClass == WorkspaceClass.Phone))
        {
            throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.WorkspaceClassInvalid,
                "The presentation mode does not belong to this workspace class.");
        }

        // The aggregate owns the state matrix, so the stored row can only ever hold a
        // combination the domain already accepted.
        var layout = ToDomain(row, workspaceClass);
        switch (mode)
        {
            case PresentationMode.PhoneEmpty:
                layout.ShowNothing();
                break;
            case PresentationMode.PhoneSingle:
                layout.ShowSingle(RequireWindowId(document.PrimaryWindowId, "primary"));
                break;
            case PresentationMode.PhoneSplit:
                layout.ShowSplit(
                    RequireWindowId(document.PrimaryWindowId, "primary"),
                    RequireWindowId(document.SecondaryWindowId, "secondary"),
                    document.SplitRatioPermille ?? 0);
                break;
            default:
                layout.Arrange(mode);
                break;
        }

        if (document.DisplayCount != row.DisplayCount)
        {
            layout.SetDisplayCount(document.DisplayCount);
        }

        row.PresentationMode = layout.PresentationMode;
        row.PrimaryWindowId = layout.PrimaryWindowId?.Value;
        row.SecondaryWindowId = layout.SecondaryWindowId?.Value;
        row.SplitRatioPermille = layout.SplitRatioPermille;
        row.DisplayCount = layout.DisplayCount;
    }

    private static DesktopLayout ToDomain(DesktopLayoutRow row, WorkspaceClass workspaceClass)
    {
        var layout = row.ClientDeviceId is null
            ? DesktopLayout.CreateShared(new DesktopLayoutId(row.Id), workspaceClass)
            : DesktopLayout.CreateForDevice(new DesktopLayoutId(row.Id), workspaceClass, row.ClientDeviceId.Value);

        foreach (var window in row.Windows.OrderBy(window => window.ZIndex))
        {
            layout.AddWindow(ToDomain(window, workspaceClass));
        }

        return layout;
    }

    private static DesktopWindow ToDomain(DesktopWindowRow row, WorkspaceClass workspaceClass)
    {
        var window = DesktopWindow.Open(
            new WindowId(row.Id),
            new Domain.Applications.ApplicationDefinitionId(row.ApplicationDefinitionId),
            row.LaunchTargetId is null ? null : new Domain.Applications.LaunchTargetId(row.LaunchTargetId.Value),
            WindowBounds.Create(row.X, row.Y, row.Width, row.Height),
            row.ZIndex,
            workspaceClass,
            row.DisplaySlot);

        if (row.State == WindowState.Minimized)
        {
            window.Minimize();
        }

        return window;
    }

    private static WindowId RequireWindowId(Guid? value, string field)
    {
        return value is null || value.Value == Guid.Empty
            ? throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.InvalidLayout,
                $"The presentation mode requires a {field} window.")
            : new WindowId(value.Value);
    }

    private static DesktopWindowRow Clone(DesktopWindowRow source) => new()
    {
        Id = source.Id,
        DesktopLayoutId = source.DesktopLayoutId,
        ApplicationDefinitionId = source.ApplicationDefinitionId,
        LaunchTargetId = source.LaunchTargetId,
        State = source.State,
        X = source.X,
        Y = source.Y,
        Width = source.Width,
        Height = source.Height,
        RestoreX = source.RestoreX,
        RestoreY = source.RestoreY,
        RestoreWidth = source.RestoreWidth,
        RestoreHeight = source.RestoreHeight,
        ZIndex = source.ZIndex,
        WorkspaceClass = source.WorkspaceClass,
        DisplaySlot = source.DisplaySlot,
        SessionReferenceId = source.SessionReferenceId,
        CreatedAtUtc = source.CreatedAtUtc,
        UpdatedAtUtc = source.UpdatedAtUtc,
        Revision = 1,
    };

    private static DesktopWindowRow ToRow(
        Guid layoutId,
        DesktopWindowContract window,
        int zIndex,
        WorkspaceClass workspaceClass,
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
            ZIndex = zIndex,
            // Never taken from the request: the layout being written decides the class.
            WorkspaceClass = workspaceClass,
            DisplaySlot = window.DisplaySlot,
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

    private static WorkspaceLayoutResponse TransientResponse(ResolvedWorkspace resolved) => new(
        WorkspaceClassName(resolved.WorkspaceClass),
        ScopeName(resolved.Scope),
        RestoreModeNames.Fresh,
        PersistenceEnabled: false,
        Revision: 0,
        new WorkspaceLayoutDocument(
            LayoutId: null,
            DefaultLayoutName,
            PresentationModeName(NeutralMode(resolved.WorkspaceClass)),
            PrimaryWindowId: null,
            SecondaryWindowId: null,
            SplitRatioPermille: null,
            DisplayCount: 1,
            UpdatedAtUtc: DateTimeOffset.MinValue,
            Windows: [],
            Widgets: []));

    private static WorkspaceLayoutResponse EmptyResponse(ResolvedWorkspace resolved) => new(
        WorkspaceClassName(resolved.WorkspaceClass),
        ScopeName(resolved.Scope),
        RestoreModeNames.Resume,
        PersistenceEnabled: true,
        Revision: 0,
        new WorkspaceLayoutDocument(
            LayoutId: null,
            DefaultLayoutName,
            PresentationModeName(NeutralMode(resolved.WorkspaceClass)),
            PrimaryWindowId: null,
            SecondaryWindowId: null,
            SplitRatioPermille: null,
            DisplayCount: 1,
            UpdatedAtUtc: DateTimeOffset.MinValue,
            Windows: [],
            Widgets: []));

    private static WorkspaceLayoutResponse ToResponse(ResolvedWorkspace resolved, DesktopLayoutRow row) => new(
        WorkspaceClassName(row.WorkspaceClass),
        ScopeName(resolved.Scope),
        RestoreModeName(resolved.RestoreMode),
        PersistenceEnabled: true,
        row.Revision,
        new WorkspaceLayoutDocument(
            row.Id,
            row.Name,
            PresentationModeName(row.PresentationMode),
            row.PrimaryWindowId,
            row.SecondaryWindowId,
            row.SplitRatioPermille,
            row.DisplayCount,
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
                    window.SessionReferenceId,
                    window.DisplaySlot))
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
                .ToArray()));

    private static void Validate(WorkspaceLayoutWriteRequest request, WorkspaceClass workspaceClass)
    {
        var document = request.Layout
            ?? throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.InvalidLayout,
                "The workspace layout document is required.");

        if (request.ExpectedRevision < 0
            || document.Windows.Count > MaximumWindows
            || document.Widgets.Count > MaximumWidgets
            || document.Name.Length is 0 or > 128)
        {
            throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.InvalidLayout,
                "The workspace layout document is invalid.");
        }

        var windowIds = new HashSet<Guid>();
        var zIndexes = new HashSet<int>();
        foreach (var window in document.Windows)
        {
            if (window.WindowId == Guid.Empty
                || window.ApplicationDefinitionId == Guid.Empty
                || window.LaunchTargetId == Guid.Empty
                || window.SessionReferenceId == Guid.Empty
                || window.ZIndex < 0
                || window.DisplaySlot < 0
                || (workspaceClass != WorkspaceClass.DesktopMulti && window.DisplaySlot != 0)
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
                throw new WorkspaceLayoutFailureException(
                    WorkspaceLayoutFailureReason.InvalidLayout,
                    "A desktop window is invalid.");
            }

            _ = ParseWindowState(window.State);
        }

        // Window z-indexes only need to be unique and non-negative. A client's stacking
        // order can leave gaps, for example after a window in the middle closes, and the
        // write re-packs the survivors into a dense 0..N-1 order by ascending z-index.
        var widgetIds = new HashSet<Guid>();
        foreach (var widget in document.Widgets)
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
                throw new WorkspaceLayoutFailureException(
                    WorkspaceLayoutFailureReason.InvalidLayout,
                    "A widget placement is invalid.");
            }
        }
    }

    private static PresentationMode NeutralMode(WorkspaceClass workspaceClass) =>
        workspaceClass == WorkspaceClass.Phone ? PresentationMode.PhoneEmpty : PresentationMode.Freeform;

    private static string HashKey(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static WorkspaceClass ParseWorkspaceClass(string value) => value switch
    {
        WorkspaceClassNames.Phone => WorkspaceClass.Phone,
        WorkspaceClassNames.Tablet => WorkspaceClass.Tablet,
        WorkspaceClassNames.DesktopSingle => WorkspaceClass.DesktopSingle,
        WorkspaceClassNames.DesktopMulti => WorkspaceClass.DesktopMulti,
        _ => throw new WorkspaceLayoutFailureException(
            WorkspaceLayoutFailureReason.WorkspaceClassInvalid,
            "The workspace class is not supported."),
    };

    private static string WorkspaceClassName(WorkspaceClass value) => value switch
    {
        WorkspaceClass.Phone => WorkspaceClassNames.Phone,
        WorkspaceClass.Tablet => WorkspaceClassNames.Tablet,
        WorkspaceClass.DesktopSingle => WorkspaceClassNames.DesktopSingle,
        WorkspaceClass.DesktopMulti => WorkspaceClassNames.DesktopMulti,
        _ => throw new InvalidOperationException("Unknown workspace class."),
    };

    private static string ScopeName(LayoutScope scope) =>
        scope == LayoutScope.Device ? LayoutScopeNames.Device : LayoutScopeNames.Shared;

    private static string RestoreModeName(RestoreMode mode) =>
        mode == RestoreMode.Fresh ? RestoreModeNames.Fresh : RestoreModeNames.Resume;

    private static PresentationMode ParsePresentationMode(string value) => value switch
    {
        PresentationModeNames.Freeform => PresentationMode.Freeform,
        PresentationModeNames.Tiled => PresentationMode.Tiled,
        PresentationModeNames.PhoneEmpty => PresentationMode.PhoneEmpty,
        PresentationModeNames.PhoneSingle => PresentationMode.PhoneSingle,
        PresentationModeNames.PhoneSplit => PresentationMode.PhoneSplit,
        _ => throw new WorkspaceLayoutFailureException(
            WorkspaceLayoutFailureReason.InvalidLayout,
            "The presentation mode is not supported."),
    };

    private static string PresentationModeName(PresentationMode mode) => mode switch
    {
        PresentationMode.Freeform => PresentationModeNames.Freeform,
        PresentationMode.Tiled => PresentationModeNames.Tiled,
        PresentationMode.PhoneEmpty => PresentationModeNames.PhoneEmpty,
        PresentationMode.PhoneSingle => PresentationModeNames.PhoneSingle,
        PresentationMode.PhoneSplit => PresentationModeNames.PhoneSplit,
        _ => throw new InvalidOperationException("Unknown presentation mode."),
    };

    private static WindowState ParseWindowState(string state) => state switch
    {
        "normal" => WindowState.Normal,
        "minimized" => WindowState.Minimized,
        "maximized" => WindowState.Maximized,
        "snapped-left" => WindowState.SnappedLeft,
        "snapped-right" => WindowState.SnappedRight,
        "snapped-top-left" => WindowState.SnappedTopLeft,
        "snapped-top-right" => WindowState.SnappedTopRight,
        "snapped-bottom-left" => WindowState.SnappedBottomLeft,
        "snapped-bottom-right" => WindowState.SnappedBottomRight,
        "full-screen" => WindowState.FullScreen,
        _ => throw new WorkspaceLayoutFailureException(
            WorkspaceLayoutFailureReason.InvalidLayout,
            "The desktop window state is invalid."),
    };

    private static string WindowStateName(WindowState state) => state switch
    {
        WindowState.Normal => "normal",
        WindowState.Minimized => "minimized",
        WindowState.Maximized => "maximized",
        WindowState.SnappedLeft => "snapped-left",
        WindowState.SnappedRight => "snapped-right",
        WindowState.SnappedTopLeft => "snapped-top-left",
        WindowState.SnappedTopRight => "snapped-top-right",
        WindowState.SnappedBottomLeft => "snapped-bottom-left",
        WindowState.SnappedBottomRight => "snapped-bottom-right",
        WindowState.FullScreen => "full-screen",
        _ => throw new InvalidOperationException("Unknown desktop window state."),
    };
}
