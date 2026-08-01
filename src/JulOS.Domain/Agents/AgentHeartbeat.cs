namespace JulOS.Domain.Agents;

/// <summary>
/// The moment Server last heard from an Agent on its control connection.
/// </summary>
/// <remarks>
/// This is connectivity evidence only, never a host observation. It carries no payload
/// besides the moment itself, and the only way to produce one is <see cref="Now"/> reading
/// the current time: there is no constructor or factory that accepts an externally
/// supplied value. That is deliberate. A last-seen moment answers "is this Agent still
/// reachable", never "what is true about the host right now"; a CPU load, a free-space
/// figure or any other measurement is reported through a capability-specific contract
/// and never attached to this type.
/// </remarks>
public readonly record struct AgentHeartbeat
{
    private AgentHeartbeat(DateTimeOffset atUtc) => this.AtUtc = atUtc;

    /// <summary>The moment Server received the signal this heartbeat records.</summary>
    public DateTimeOffset AtUtc { get; }

    /// <summary>Records a heartbeat at the current time.</summary>
    /// <param name="timeProvider">The clock the heartbeat is recorded from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public static AgentHeartbeat Now(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new AgentHeartbeat(timeProvider.GetUtcNow());
    }
}
