using JulOS.Domain.Primitives;

namespace JulOS.Domain.Agents;

/// <summary>
/// One enrolled Agent: its identity, reported platform, connection state and revocation.
/// </summary>
/// <remarks>
/// <para>
/// Enrollment issues the Agent client credentials it authenticates its control connection
/// with (see <c>TECHNICAL_SPECIFICATION.md</c> section 7), but this record never stores or
/// references the credential, key or token value itself. <c>DATA_AND_API_CONTRACTS.md</c>
/// section 2.11 defines no such field, and Domain has no mechanism to keep a secret safe;
/// the credential lease belongs to the secret-reference service described by the Secret
/// Reference glossary entry, which is the one place a value like that is allowed to exist.
/// Core only ever needs to know that a connection presenting itself as this
/// <see cref="Id"/> is currently allowed to be <see cref="AgentConnectionState.Connected"/>,
/// and that question is answered entirely by <see cref="State"/>.
/// </para>
/// <para>
/// <see cref="AgentConnectionState.Revoked"/> has no outgoing edge in the transition graph
/// enforced by <see cref="EnsureTransitionAllowed"/>, so once an Agent is revoked no later
/// call, including <see cref="Connect"/>, can move it back to
/// <see cref="AgentConnectionState.Connected"/>.
/// </para>
/// </remarks>
public sealed class Agent
{
    private const int MaximumLabelLength = 256;

    private static readonly Dictionary<AgentConnectionState, HashSet<AgentConnectionState>> AllowedTransitions = new()
    {
        [AgentConnectionState.Enrolled] = new() { AgentConnectionState.Connected, AgentConnectionState.Revoked },
        [AgentConnectionState.Connected] = new() { AgentConnectionState.Disconnected, AgentConnectionState.Revoked },
        [AgentConnectionState.Disconnected] = new() { AgentConnectionState.Connected, AgentConnectionState.Revoked },
        [AgentConnectionState.Revoked] = new(),
    };

    private Agent(
        AgentId id,
        AgentMachineIdentity machineIdentity,
        string name,
        string operatingSystem,
        string architecture,
        string version,
        DateTimeOffset enrolledAtUtc)
    {
        this.Id = id;
        this.MachineIdentity = machineIdentity;
        this.Name = name;
        this.OperatingSystem = operatingSystem;
        this.Architecture = architecture;
        this.Version = version;
        this.State = AgentConnectionState.Enrolled;
        this.EnrolledAtUtc = enrolledAtUtc;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this enrollment record.</summary>
    public AgentId Id { get; }

    /// <summary>The stable identity of the host this Agent runs on.</summary>
    public AgentMachineIdentity MachineIdentity { get; }

    /// <summary>The label shown to the user. Never part of identity.</summary>
    public string Name { get; private set; }

    /// <summary>The operating system the Agent last reported running on.</summary>
    public string OperatingSystem { get; private set; }

    /// <summary>The processor architecture the Agent last reported running on.</summary>
    public string Architecture { get; private set; }

    /// <summary>The Agent binary version last reported.</summary>
    public string Version { get; private set; }

    /// <summary>The current connection lifecycle state.</summary>
    public AgentConnectionState State { get; private set; }

    /// <summary>When enrollment completed.</summary>
    public DateTimeOffset EnrolledAtUtc { get; }

    /// <summary>The most recent heartbeat, or <see langword="null"/> before the Agent has ever connected.</summary>
    public AgentHeartbeat? LastSeen { get; private set; }

    /// <summary>When an administrator revoked the Agent, or <see langword="null"/> while it is not revoked.</summary>
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Completes enrollment for a host. The record starts <see cref="AgentConnectionState.Enrolled"/>.</summary>
    /// <param name="id">The generated identity of the new enrollment record.</param>
    /// <param name="machineIdentity">The stable identity of the host the Agent runs on.</param>
    /// <param name="name">The label shown to the user.</param>
    /// <param name="operatingSystem">The operating system the Agent reports running on.</param>
    /// <param name="architecture">The processor architecture the Agent reports running on.</param>
    /// <param name="version">The Agent binary version.</param>
    /// <param name="timeProvider">The clock enrollment is timestamped from.</param>
    /// <exception cref="DomainRuleViolationException">A reported value is not a usable label.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public static Agent Enroll(
        AgentId id,
        AgentMachineIdentity machineIdentity,
        string name,
        string operatingSystem,
        string architecture,
        string version,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new Agent(
            id,
            machineIdentity,
            ValidatedLabel(name, "agent.name.invalid", "An Agent label"),
            ValidatedLabel(operatingSystem, "agent.operating_system.invalid", "A reported operating system"),
            ValidatedLabel(architecture, "agent.architecture.invalid", "A reported architecture"),
            ValidatedLabel(version, "agent.version.invalid", "A reported Agent version"),
            timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Establishes the control connection, including a reconnect after a disconnect.
    /// </summary>
    /// <param name="timeProvider">The clock the connection and heartbeat are timestamped from.</param>
    /// <exception cref="DomainRuleViolationException">The Agent is revoked, or is already connected.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public void Connect(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.EnsureTransitionAllowed(AgentConnectionState.Connected);

        this.State = AgentConnectionState.Connected;
        this.LastSeen = AgentHeartbeat.Now(timeProvider);
        this.Revision = this.Revision.Next();
    }

    /// <summary>Records a signal from an already-connected Agent without changing its state.</summary>
    /// <param name="timeProvider">The clock the heartbeat is timestamped from.</param>
    /// <exception cref="DomainRuleViolationException">The Agent is not currently connected.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public void Heartbeat(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (this.State != AgentConnectionState.Connected)
        {
            throw new DomainRuleViolationException(
                "agent.heartbeat.not_connected",
                $"Agent '{this.Id.Value}' cannot record a heartbeat while it is {this.State}.");
        }

        this.LastSeen = AgentHeartbeat.Now(timeProvider);
        this.Revision = this.Revision.Next();
    }

    /// <summary>Ends the control connection. The Agent may reconnect later with <see cref="Connect"/>.</summary>
    /// <exception cref="DomainRuleViolationException">The Agent is not currently connected.</exception>
    public void Disconnect()
    {
        this.EnsureTransitionAllowed(AgentConnectionState.Disconnected);

        this.State = AgentConnectionState.Disconnected;
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Revokes the Agent. A revoked Agent can never reconnect, regardless of what credential
    /// it presents.
    /// </summary>
    /// <param name="timeProvider">The clock revocation is timestamped from.</param>
    /// <exception cref="DomainRuleViolationException">The Agent is already revoked.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public void Revoke(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.EnsureTransitionAllowed(AgentConnectionState.Revoked);

        this.State = AgentConnectionState.Revoked;
        this.RevokedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    /// <summary>Renames the Agent. Identity is unaffected.</summary>
    /// <param name="name">The new label shown to the user.</param>
    /// <exception cref="DomainRuleViolationException"><paramref name="name"/> is not a usable label.</exception>
    public void Rename(string name)
    {
        this.Name = ValidatedLabel(name, "agent.name.invalid", "An Agent label");
        this.Revision = this.Revision.Next();
    }

    /// <summary>Records the platform the Agent reported on its current connection.</summary>
    /// <param name="operatingSystem">The operating system the Agent reports running on.</param>
    /// <param name="architecture">The processor architecture the Agent reports running on.</param>
    /// <param name="version">The Agent binary version.</param>
    /// <exception cref="DomainRuleViolationException">A reported value is not a usable label.</exception>
    public void ReportPlatform(string operatingSystem, string architecture, string version)
    {
        this.OperatingSystem = ValidatedLabel(operatingSystem, "agent.operating_system.invalid", "A reported operating system");
        this.Architecture = ValidatedLabel(architecture, "agent.architecture.invalid", "A reported architecture");
        this.Version = ValidatedLabel(version, "agent.version.invalid", "A reported Agent version");
        this.Revision = this.Revision.Next();
    }

    private static string ValidatedLabel(string value, string code, string description)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumLabelLength || value.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                code,
                $"{description} is non-empty, contains no control character and is at most {MaximumLabelLength} characters.");
        }

        return value;
    }

    private void EnsureTransitionAllowed(AgentConnectionState target)
    {
        if (this.State == AgentConnectionState.Revoked)
        {
            throw new DomainRuleViolationException(
                "agent.revoked",
                $"Agent '{this.Id.Value}' was revoked and cannot transition to '{target}'.");
        }

        if (!AllowedTransitions[this.State].Contains(target))
        {
            throw new DomainRuleViolationException(
                "agent.transition.invalid",
                $"Agent '{this.Id.Value}' cannot move from '{this.State}' to '{target}'.");
        }
    }
}
