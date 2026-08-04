using JulOS.Contracts.Remote;

namespace JulOS.Application.Remote;

/// <summary>Disconnects one user- and package-owned Remote session.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="Request">Validated disconnect request.</param>
public sealed record DisconnectRemoteSessionCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    DisconnectRemoteSessionRequest Request);

/// <summary>Applies one explicit presentation-window detach behavior.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="Request">Validated detach request.</param>
public sealed record DetachRemoteSessionCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    DetachRemoteSessionRequest Request);

/// <summary>Summary of one bounded Remote lifecycle reconciliation pass.</summary>
/// <param name="Examined">Number of due sessions examined.</param>
/// <param name="Expired">Number transitioned to expired.</param>
/// <param name="Cleaned">Number of runtimes removed or already absent.</param>
/// <param name="CleanupFailures">Number of runtime removals that still require operator attention.</param>
public sealed record RemoteLifecycleReconciliationResult(
    int Examined,
    int Expired,
    int Cleaned,
    int CleanupFailures);

/// <summary>Owns explicit disconnect, detach, expiry and terminal runtime cleanup.</summary>
public interface IRemoteSessionLifecycleService
{
    /// <summary>Disconnects one active session and removes its runtime idempotently.</summary>
    Task<RemoteSessionResponse> DisconnectAsync(
        DisconnectRemoteSessionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Applies the caller-selected effect of detaching a presentation window.</summary>
    Task<RemoteSessionResponse> DetachAsync(
        DetachRemoteSessionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Expires due sessions and reconciles terminal runtime cleanup in a bounded pass.</summary>
    Task<RemoteLifecycleReconciliationResult> ReconcileDueAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
