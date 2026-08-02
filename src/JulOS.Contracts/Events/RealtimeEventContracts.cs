using System.Text.Json;

namespace JulOS.Contracts.Events;

/// <summary>Stable members of the JulOS real-time event protocol.</summary>
public static class RealtimeEventContract
{
    /// <summary>The authenticated SignalR hub path.</summary>
    public const string HubPath = "/hubs/events";

    /// <summary>The SignalR client method that receives one envelope.</summary>
    public const string ClientMethod = "event";

    /// <summary>The current event-envelope contract version.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// A small notification that identifies changed state while the HTTP API remains authoritative.
/// </summary>
/// <param name="EventId">The unique event identifier used for client deduplication.</param>
/// <param name="EventType">The stable dotted event name.</param>
/// <param name="ContractVersion">The envelope contract version.</param>
/// <param name="OccurredAtUtc">When the event was published.</param>
/// <param name="CorrelationId">The operation correlation identifier.</param>
/// <param name="ResourceId">The stable changed-resource identity.</param>
/// <param name="Revision">The new resource revision when the resource is revisioned.</param>
/// <param name="Payload">A small event-specific payload; authoritative state is read through HTTP.</param>
public sealed record RealtimeEventEnvelope(
    Guid EventId,
    string EventType,
    int ContractVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string ResourceId,
    int? Revision,
    JsonElement Payload);
