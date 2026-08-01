namespace JulOS.Domain.Agents;

/// <summary>
/// The connection lifecycle state of one Agent.
/// </summary>
/// <remarks>
/// The valid moves between these states are enforced by <see cref="Agent"/>.
/// <see cref="Revoked"/> is terminal: nothing an Agent binary can present ever moves a
/// revoked record back to <see cref="Connected"/>, which is what makes a stolen or leaked
/// credential useless once an administrator revokes the Agent it belonged to.
/// </remarks>
public enum AgentConnectionState
{
    /// <summary>Enrollment completed. The Agent has never established a control connection.</summary>
    Enrolled = 1,

    /// <summary>The Agent currently holds an authenticated control connection.</summary>
    Connected = 2,

    /// <summary>The control connection ended. The Agent may reconnect later.</summary>
    Disconnected = 3,

    /// <summary>An administrator revoked the Agent. No further connection is ever accepted.</summary>
    Revoked = 4,
}
