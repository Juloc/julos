using System.Text.Json;

namespace JulOS.Application.Events;

/// <summary>One small notification published after authoritative state changed.</summary>
public sealed record RealtimeEventNotification(
    string EventType,
    string CorrelationId,
    string ResourceId,
    int? Revision,
    JsonElement Payload);

/// <summary>Publishes versioned control-plane events without exposing a transport to Application.</summary>
public interface IRealtimeEventPublisher
{
    /// <summary>Publishes one event to authenticated connected clients.</summary>
    Task PublishAsync(
        RealtimeEventNotification notification,
        CancellationToken cancellationToken = default);
}
