using JulOS.Contracts.Layouts;

namespace JulOS.Application.Layouts;

/// <summary>Reads and replaces the current user's default layout per viewport.</summary>
public interface IDesktopLayoutService
{
    /// <summary>Reads the default layout for one user and viewport class.</summary>
    /// <param name="userId">Owning user identity.</param>
    /// <param name="viewport">Desktop, tablet or mobile viewport name.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The authoritative persisted layout.</returns>
    Task<DesktopLayoutResponse> ReadAsync(
        Guid userId,
        string viewport,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the default layout using optimistic concurrency.</summary>
    /// <param name="userId">Owning user identity.</param>
    /// <param name="viewport">Desktop, tablet or mobile viewport name.</param>
    /// <param name="request">Complete replacement layout and expected revision.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>The saved authoritative layout.</returns>
    Task<DesktopLayoutResponse> SaveAsync(
        Guid userId,
        string viewport,
        SaveDesktopLayoutRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable failure reasons for desktop layout operations.</summary>
public enum DesktopLayoutFailureReason
{
    /// <summary>The submitted layout violates geometry, identity or viewport rules.</summary>
    InvalidLayout = 1,

    /// <summary>The requested layout does not exist.</summary>
    NotFound = 2,
}

/// <summary>Caller-safe desktop layout application failure.</summary>
public sealed class DesktopLayoutFailureException : Exception
{
    /// <summary>Creates a desktop layout failure.</summary>
    /// <param name="reason">Stable failure reason.</param>
    /// <param name="innerException">Optional server-side cause.</param>
    public DesktopLayoutFailureException(
        DesktopLayoutFailureReason reason,
        Exception? innerException = null)
        : base(reason.ToString(), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>Gets the stable failure reason.</summary>
    public DesktopLayoutFailureReason Reason { get; }
}
