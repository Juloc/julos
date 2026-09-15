using JulOS.Contracts.Layouts;

namespace JulOS.Application.Layouts;

/// <summary>
/// Resolves and replaces the layout a session sees for one workspace class.
/// </summary>
/// <remarks>
/// Resolution happens on the server, from the authenticated user, the owner-scoped device
/// cookie and the requested workspace class. A caller cannot name a layout identity, a
/// client device or a scope: choosing the shared or the device layout is the consequence
/// of that device's stored preference, not of request data.
/// </remarks>
public interface IWorkspaceLayoutService
{
    /// <summary>Resolves the layout for one workspace class.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="workspaceClass">Workspace class from the route.</param>
    /// <param name="presentedKey">The raw key from the device cookie, if the request carried one.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>
    /// The resolved layout together with the scope and restore mode it resolved to. In
    /// fresh mode the layout is transient: no identity, revision zero and persistence off.
    /// </returns>
    Task<WorkspaceLayoutResponse> ReadCurrentAsync(
        Guid userId,
        string workspaceClass,
        string? presentedKey,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the resolved layout using optimistic concurrency.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="workspaceClass">Workspace class from the route.</param>
    /// <param name="presentedKey">The raw key from the device cookie, if the request carried one.</param>
    /// <param name="request">Complete replacement layout and expected revision.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The written authoritative layout.</returns>
    /// <exception cref="WorkspaceLayoutFailureException">
    /// The resolved workspace runs in fresh mode, or the submitted layout is invalid.
    /// </exception>
    Task<WorkspaceLayoutWriteResult> WriteCurrentAsync(
        Guid userId,
        string workspaceClass,
        string? presentedKey,
        WorkspaceLayoutWriteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the multi-display layout of one user from the single-display one.
    /// </summary>
    /// <remarks>
    /// Multi-display is never inferred. A physical topology is not invented from stored
    /// geometry, so the first <c>desktop-multi</c> layout is created explicitly, either by
    /// copying <c>desktop-single</c> or by starting empty.
    /// </remarks>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="presentedKey">The raw key from the device cookie, if the request carried one.</param>
    /// <param name="copyFromDesktopSingle">Copy the single-display arrangement instead of starting empty.</param>
    /// <param name="displayCount">How many display participants the new layout is arranged for.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task<WorkspaceLayoutWriteResult> InitializeMultiDisplayAsync(
        Guid userId,
        string? presentedKey,
        bool copyFromDesktopSingle,
        int displayCount,
        CancellationToken cancellationToken = default);
}

/// <summary>A written layout and whether the write created it.</summary>
/// <param name="Layout">The authoritative layout after the write.</param>
/// <param name="Created">True when the write created the layout rather than replacing it.</param>
public sealed record WorkspaceLayoutWriteResult(WorkspaceLayoutResponse Layout, bool Created);

/// <summary>Stable failure reasons for workspace layout operations.</summary>
public enum WorkspaceLayoutFailureReason
{
    /// <summary>The submitted layout violates geometry, identity or presentation rules.</summary>
    InvalidLayout = 1,

    /// <summary>The requested layout does not exist.</summary>
    NotFound = 2,

    /// <summary>The workspace class in the route is not a workspace class.</summary>
    WorkspaceClassInvalid = 3,

    /// <summary>The resolved workspace runs in fresh mode and never persists.</summary>
    PersistenceDisabled = 4,

    /// <summary>A phone was asked to show more foreground windows than it can.</summary>
    PhoneForegroundLimitExceeded = 5,
}

/// <summary>Caller-safe workspace layout application failure.</summary>
public sealed class WorkspaceLayoutFailureException : Exception
{
    /// <summary>Creates a workspace layout failure.</summary>
    /// <param name="reason">Stable failure reason.</param>
    /// <param name="message">Caller-safe explanation.</param>
    /// <param name="innerException">Optional server-side cause.</param>
    public WorkspaceLayoutFailureException(
        WorkspaceLayoutFailureReason reason,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        this.Reason = reason;
    }

    /// <summary>Gets the stable failure reason.</summary>
    public WorkspaceLayoutFailureReason Reason { get; }

    /// <summary>Gets the stable error code the caller sees.</summary>
    public string Code => this.Reason switch
    {
        WorkspaceLayoutFailureReason.PersistenceDisabled => WorkspaceLayoutErrorCodes.PersistenceDisabled,
        WorkspaceLayoutFailureReason.WorkspaceClassInvalid => WorkspaceLayoutErrorCodes.WorkspaceClassInvalid,
        WorkspaceLayoutFailureReason.PhoneForegroundLimitExceeded =>
            WorkspaceLayoutErrorCodes.PhoneForegroundLimitExceeded,
        _ => WorkspaceLayoutErrorCodes.Invalid,
    };
}
