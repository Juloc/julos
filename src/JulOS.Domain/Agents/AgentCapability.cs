using JulOS.Domain.Primitives;

namespace JulOS.Domain.Agents;

/// <summary>
/// One capability one Agent advertises, and whether it is currently enabled.
/// </summary>
/// <remarks>
/// A capability record stands on its own, referencing <see cref="AgentId"/> rather than
/// living inside a collection owned by <see cref="Agent"/>, exactly like
/// <see cref="JulOS.Domain.Applications.LaunchTarget"/> references its owning application
/// without that application holding a back-collection. <see cref="Refresh"/> never changes
/// <see cref="Enabled"/>, so an administrator's decision to disable a capability survives
/// the Agent re-advertising it on the next reconnect.
/// </remarks>
public sealed class AgentCapability
{
    private AgentCapability(
        AgentCapabilityId id,
        AgentId agentId,
        CapabilityName name,
        CapabilityVersion version,
        CapabilityVersion metadataVersion,
        CapabilityMetadata metadata,
        DateTimeOffset observedAtUtc)
    {
        this.Id = id;
        this.AgentId = agentId;
        this.Name = name;
        this.Version = version;
        this.MetadataVersion = metadataVersion;
        this.Metadata = metadata;
        this.ObservedAtUtc = observedAtUtc;
        this.Enabled = true;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this capability record.</summary>
    public AgentCapabilityId Id { get; }

    /// <summary>The Agent that advertises this capability.</summary>
    public AgentId AgentId { get; }

    /// <summary>The capability family being advertised.</summary>
    public CapabilityName Name { get; }

    /// <summary>The contract version of the capability implementation.</summary>
    public CapabilityVersion Version { get; }

    /// <summary>Whether the capability may currently be requested through the broker.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The schema version <see cref="Metadata"/> is encoded with.</summary>
    public CapabilityVersion MetadataVersion { get; private set; }

    /// <summary>The capability-specific metadata payload, opaque to Core.</summary>
    public CapabilityMetadata Metadata { get; private set; }

    /// <summary>When the Agent most recently reported this capability.</summary>
    public DateTimeOffset ObservedAtUtc { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Records a capability an Agent has advertised. It starts enabled.</summary>
    /// <param name="id">The generated identity of the new capability record.</param>
    /// <param name="agentId">The Agent advertising the capability.</param>
    /// <param name="name">The capability family being advertised.</param>
    /// <param name="version">The contract version of the capability implementation.</param>
    /// <param name="metadataVersion">The schema version <paramref name="metadata"/> is encoded with.</param>
    /// <param name="metadata">The capability-specific metadata payload.</param>
    /// <param name="timeProvider">The clock the record is timestamped from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public static AgentCapability Advertise(
        AgentCapabilityId id,
        AgentId agentId,
        CapabilityName name,
        CapabilityVersion version,
        CapabilityVersion metadataVersion,
        CapabilityMetadata metadata,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new AgentCapability(id, agentId, name, version, metadataVersion, metadata, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Records that the Agent reported this capability again, refreshing its metadata.
    /// </summary>
    /// <remarks>
    /// A refresh never changes <see cref="Enabled"/>. That is what keeps an administrator's
    /// decision to disable a capability in place across every later reconnect, instead of the
    /// capability silently reappearing enabled.
    /// </remarks>
    /// <param name="metadataVersion">The schema version <paramref name="metadata"/> is encoded with.</param>
    /// <param name="metadata">The capability-specific metadata payload.</param>
    /// <param name="timeProvider">The clock the observation is timestamped from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public void Refresh(CapabilityVersion metadataVersion, CapabilityMetadata metadata, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.MetadataVersion = metadataVersion;
        this.Metadata = metadata;
        this.ObservedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    /// <summary>Stops offering the capability through the broker without forgetting it.</summary>
    public void Disable()
    {
        this.Enabled = false;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Offers the capability through the broker again.</summary>
    public void Enable()
    {
        this.Enabled = true;
        this.Revision = this.Revision.Next();
    }
}
