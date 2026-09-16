namespace JulOS.Contracts.Devices;

/// <summary>Stable background-mode names.</summary>
public static class BackgroundModeNames
{
    /// <summary>Stop timers, polling, rendering and display connections.</summary>
    public const string Suspend = "suspend";

    /// <summary>Keep the surface running while the JulOS page is alive; best effort.</summary>
    public const string KeepSurfaceActive = "keep-surface-active";

    /// <summary>Every background mode, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } = [Suspend, KeepSurfaceActive];
}

/// <summary>What happens to one application's surface when it leaves the visible foreground.</summary>
/// <param name="ApplicationDefinitionId">The application the preference applies to.</param>
/// <param name="WorkspaceClass">The workspace class the preference applies to.</param>
/// <param name="LayoutScope">"shared" or "device", describing which preference answered.</param>
/// <param name="BackgroundMode">The resolved mode.</param>
/// <param name="SupportsKeepSurfaceActive">Whether the application declares it can stay active.</param>
/// <param name="Revision">Concurrency revision; zero when nothing is stored yet.</param>
public sealed record ApplicationExecutionPreferenceResponse(
    Guid ApplicationDefinitionId,
    string WorkspaceClass,
    string LayoutScope,
    string BackgroundMode,
    bool SupportsKeepSurfaceActive,
    int Revision);

/// <summary>Sets what happens to one application's surface in the background.</summary>
/// <param name="BackgroundMode">"suspend" or "keep-surface-active".</param>
/// <param name="ExpectedRevision">The revision the caller based the change on; null creates.</param>
/// <remarks>
/// Only the authenticated owner writes this. An application can neither request nor persist
/// a mode for itself, and a mode its manifest does not declare is refused rather than
/// stored and quietly ignored later.
/// </remarks>
public sealed record UpdateApplicationExecutionPreferenceRequest(
    string BackgroundMode,
    int? ExpectedRevision);
