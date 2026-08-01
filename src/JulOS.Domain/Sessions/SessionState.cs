namespace JulOS.Domain.Sessions;

/// <summary>
/// The protocol-neutral state of one session reference.
/// </summary>
/// <remarks>
/// These are the only states Core knows. Which transport carried the connection, and
/// which protocol adapter served it, is decided entirely outside this type.
/// </remarks>
public enum SessionState
{
    /// <summary>The session reference was created but nothing has connected yet.</summary>
    Requested,

    /// <summary>A runtime interaction is currently connected.</summary>
    Connected,

    /// <summary>The runtime interaction disconnected. The session reference may still reconnect.</summary>
    Disconnected,

    /// <summary>The session reference was explicitly paused and is not currently interactive.</summary>
    Suspended,

    /// <summary>The session reference reached its final state and cannot resume.</summary>
    Ended,
}
