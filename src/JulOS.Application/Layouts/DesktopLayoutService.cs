using JulOS.Contracts.Layouts;

namespace JulOS.Application.Layouts;

/// <summary>Reads and replaces the current user's default layout per viewport.</summary>
public interface IDesktopLayoutService
{
    Task<DesktopLayoutResponse> ReadAsync(
        Guid userId,
        string viewport,
        CancellationToken cancellationToken = default);

    Task<DesktopLayoutResponse> SaveAsync(
        Guid userId,
        string viewport,
        SaveDesktopLayoutRequest request,
        CancellationToken cancellationToken = default);
}

public enum DesktopLayoutFailureReason
{
    InvalidLayout = 1,
    NotFound = 2,
}

public sealed class DesktopLayoutFailureException : Exception
{
    public DesktopLayoutFailureException(
        DesktopLayoutFailureReason reason,
        Exception? innerException = null)
        : base(reason.ToString(), innerException)
    {
        this.Reason = reason;
    }

    public DesktopLayoutFailureReason Reason { get; }
}
