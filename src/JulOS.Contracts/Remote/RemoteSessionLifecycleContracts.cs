namespace JulOS.Contracts.Remote;

/// <summary>Additional lifecycle operation names for the Remote session capability.</summary>
public static class RemoteSessionLifecycleCapabilityContract
{
    /// <summary>Explicitly disconnects one active Remote session.</summary>
    public const string DisconnectOperation = "disconnect";

    /// <summary>Applies an explicit window-detach behavior without guessing session intent.</summary>
    public const string DetachOperation = "detach";

    /// <summary>Authorizes a new presentation attempt for an active session.</summary>
    public const string ResumeOperation = "resume";
}

/// <summary>Supported effects when a presentation window detaches from a Remote session.</summary>
public static class RemoteWindowDetachBehaviors
{
    /// <summary>Leave the provider runtime and session active while revoking presentation access.</summary>
    public const string KeepActive = "keep-active";

    /// <summary>Disconnect the session and remove its provider runtime.</summary>
    public const string Disconnect = "disconnect";
}

/// <summary>Explicitly disconnects one Remote session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
/// <param name="Reason">Optional caller-safe reason.</param>
public sealed record DisconnectRemoteSessionRequest(
    Guid SessionId,
    long ExpectedRevision,
    string? Reason);

/// <summary>Applies the caller-selected effect of detaching a presentation window.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
/// <param name="Behavior">One value from <see cref="RemoteWindowDetachBehaviors"/>.</param>
public sealed record DetachRemoteSessionRequest(
    Guid SessionId,
    long ExpectedRevision,
    string Behavior);

/// <summary>Authorizes a new presentation attempt for an active Remote session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
public sealed record ResumeRemoteSessionRequest(
    Guid SessionId,
    long ExpectedRevision);
