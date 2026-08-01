namespace JulOS.Domain.Sessions;

/// <summary>
/// The effect that closing the presentation window has on a session reference.
/// </summary>
/// <remarks>
/// Window lifecycle and session lifecycle are separate concerns. Closing a window never
/// implicitly ends a session reference; it selects exactly one of these explicit effects,
/// applied through <see cref="SessionReference.ApplyWindowClosed"/>.
/// </remarks>
public enum SessionLifecyclePolicy
{
    /// <summary>Closing the window disconnects the session reference. It may reconnect later.</summary>
    DisconnectOnWindowClose,

    /// <summary>Closing the window suspends the session reference until it is resumed.</summary>
    SuspendOnWindowClose,

    /// <summary>Closing the window ends the session reference.</summary>
    TerminateOnWindowClose,
}
