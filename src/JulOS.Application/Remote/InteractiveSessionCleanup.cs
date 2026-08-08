namespace JulOS.Application.Remote;

/// <summary>Reconciles terminal package-owned interactive runtimes after Remote lifecycle completion.</summary>
public interface IInteractiveSessionCleanupService
{
    /// <summary>Reconciles a bounded number of terminal interactive sessions.</summary>
    Task<InteractiveSessionCleanupResult> ReconcileAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Summary of one bounded interactive-session cleanup pass.</summary>
/// <param name="Examined">Terminal sessions examined.</param>
/// <param name="Cleaned">Runtime and secret pairs removed.</param>
/// <param name="Failures">Pairs that require a later retry.</param>
public sealed record InteractiveSessionCleanupResult(int Examined, int Cleaned, int Failures);
