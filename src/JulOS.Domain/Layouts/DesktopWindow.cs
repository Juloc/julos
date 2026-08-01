using JulOS.Domain.Applications;

namespace JulOS.Domain.Layouts;

/// <summary>
/// One window stored in a desktop layout.
/// </summary>
/// <remarks>
/// This record is presentation state. A window may reference a runtime session, but its
/// existence is never proof that the session is still alive, and closing it does not
/// decide the session's fate. That separation is decision <c>D018</c>.
/// </remarks>
public sealed class DesktopWindow
{
    private WindowState? stateBeforeMinimize;

    private DesktopWindow(
        WindowId id,
        ApplicationDefinitionId applicationId,
        LaunchTargetId? launchTargetId,
        WindowBounds bounds,
        int zIndex)
    {
        this.Id = id;
        this.ApplicationId = applicationId;
        this.LaunchTargetId = launchTargetId;
        this.Bounds = bounds;
        this.RestoreBounds = bounds;
        this.ZIndex = zIndex;
        this.State = WindowState.Normal;
    }

    /// <summary>The generated identity of the window.</summary>
    public WindowId Id { get; }

    /// <summary>The application the window shows.</summary>
    public ApplicationDefinitionId ApplicationId { get; }

    /// <summary>The target the application was opened against, when it was opened against one.</summary>
    public LaunchTargetId? LaunchTargetId { get; }

    /// <summary>How the window is currently presented.</summary>
    public WindowState State { get; private set; }

    /// <summary>Where the window currently is.</summary>
    public WindowBounds Bounds { get; private set; }

    /// <summary>The bounds to return to when leaving a maximized, snapped or full-screen state.</summary>
    public WindowBounds RestoreBounds { get; private set; }

    /// <summary>The stacking position. Higher is nearer the front.</summary>
    public int ZIndex { get; internal set; }

    /// <summary>Opens a window in the normal state at the given bounds.</summary>
    public static DesktopWindow Open(
        WindowId id,
        ApplicationDefinitionId applicationId,
        LaunchTargetId? launchTargetId,
        WindowBounds bounds,
        int zIndex) =>
        new(id, applicationId, launchTargetId, bounds, zIndex);

    /// <summary>Moves or resizes a window the user is dragging.</summary>
    /// <remarks>
    /// A window that is snapped, maximized, full-screen or minimized does not own its
    /// bounds. Accepting the move silently would leave the stored state claiming a
    /// geometry the window does not have.
    /// </remarks>
    /// <exception cref="DomainRuleViolationException">The window does not currently own its bounds.</exception>
    public void MoveTo(WindowBounds bounds)
    {
        if (this.State != WindowState.Normal)
        {
            throw new DomainRuleViolationException(
                "layout.window.bounds_not_owned",
                $"A window in state '{this.State}' does not own its bounds. Restore it first.");
        }

        this.Bounds = bounds;
        this.RestoreBounds = bounds;
    }

    /// <summary>
    /// Applies a state whose geometry comes from the usable area, remembering where to return to.
    /// </summary>
    /// <exception cref="DomainRuleViolationException">The state does not fix the window's geometry.</exception>
    public void ApplyFixedState(WindowState state, UsableArea usableArea)
    {
        var bounds = SnapGeometry.BoundsFor(state, usableArea)
            ?? throw new DomainRuleViolationException(
                "layout.window.state_has_no_geometry",
                $"State '{state}' does not fix the window's geometry; use MoveTo or Restore instead.");

        if (this.State == WindowState.Normal)
        {
            this.RestoreBounds = this.Bounds;
        }

        this.stateBeforeMinimize = null;
        this.State = state;
        this.Bounds = bounds;
    }

    /// <summary>Hides the window, remembering how it was presented.</summary>
    public void Minimize()
    {
        if (this.State == WindowState.Minimized)
        {
            return;
        }

        this.stateBeforeMinimize = this.State;
        this.State = WindowState.Minimized;
    }

    /// <summary>
    /// Shows a minimized window again, exactly as it was presented before.
    /// </summary>
    /// <remarks>
    /// A maximized window that was minimized comes back maximized. Returning it to the
    /// normal state instead would silently discard what the user had set up.
    /// </remarks>
    /// <exception cref="DomainRuleViolationException">The window is not minimized.</exception>
    public void Unminimize(UsableArea usableArea)
    {
        if (this.State != WindowState.Minimized)
        {
            throw new DomainRuleViolationException(
                "layout.window.not_minimized",
                "Only a minimized window can be shown again.");
        }

        var previous = this.stateBeforeMinimize ?? WindowState.Normal;

        this.stateBeforeMinimize = null;
        this.State = previous;
        this.Bounds = SnapGeometry.BoundsFor(previous, usableArea) ?? this.RestoreBounds;
    }

    /// <summary>Returns the window to the bounds it owned before it was fixed or hidden.</summary>
    public void Restore()
    {
        this.stateBeforeMinimize = null;
        this.State = WindowState.Normal;
        this.Bounds = this.RestoreBounds;
    }
}
