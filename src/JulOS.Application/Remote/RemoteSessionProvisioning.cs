namespace JulOS.Application.Remote;

/// <summary>Summary of one bounded durable Remote provisioning reconciliation pass.</summary>
/// <param name="Examined">Number of requested or provisioning sessions examined.</param>
/// <param name="Progressed">Number advanced by the provider provisioner.</param>
/// <param name="Skipped">Number already changed by another reconciler or caller.</param>
public sealed record RemoteProvisioningReconciliationResult(
    int Examined,
    int Progressed,
    int Skipped);

/// <summary>Resumes durable Remote runtime provisioning independently from the initiating client request.</summary>
public interface IRemoteSessionProvisioningReconciler
{
    /// <summary>Processes a bounded batch of requested or interrupted provisioning sessions.</summary>
    Task<RemoteProvisioningReconciliationResult> ReconcilePendingAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Coalesces wake-up signals for the durable Remote provisioning worker.</summary>
public interface IRemoteSessionProvisioningSignal
{
    /// <summary>Requests a provisioning reconciliation pass without carrying session state in memory.</summary>
    void Signal();

    /// <summary>Waits until provisioning work may be available.</summary>
    ValueTask WaitAsync(CancellationToken cancellationToken = default);
}
