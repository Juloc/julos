namespace JulOS.Application.Concurrency;

/// <summary>
/// Signals that a mutation used a stale resource revision and therefore was not applied.
/// </summary>
/// <remarks>
/// The current revision is absent only when the conflicting row was deleted after it was read.
/// Callers must refresh authoritative state before deciding whether to retry the intended change.
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>Creates a conflict with the revision currently stored by the platform.</summary>
    /// <param name="currentRevision">The current revision, or <see langword="null"/> when the row no longer exists.</param>
    /// <param name="innerException">The persistence exception retained for server-side diagnostics.</param>
    public ConcurrencyConflictException(int? currentRevision, Exception innerException)
        : base("The resource changed after it was loaded.", innerException)
    {
        if (currentRevision is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentRevision),
                currentRevision,
                "A current revision must be positive when it is available.");
        }

        this.CurrentRevision = currentRevision;
    }

    /// <summary>The authoritative stored revision, or null when the row was deleted.</summary>
    public int? CurrentRevision { get; }
}
