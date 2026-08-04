namespace JulOS.Contracts.Remote;

/// <summary>Additional lifecycle operation names for the Remote session capability.</summary>
public static class RemoteSessionLifecycleCapabilityContract
{
    /// <summary>Explicitly disconnects one active Remote session.</summary>
    public const string DisconnectOperation = "disconnect";
}

/// <summary>Explicitly disconnects one Remote session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
/// <param name="Reason">Optional caller-safe reason.</param>
public sealed record DisconnectRemoteSessionRequest(
    Guid SessionId,
    long ExpectedRevision,
    string? Reason);
