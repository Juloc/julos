using System.Text;

using JulOS.Application.Events;
using JulOS.Contracts.Events;

using Microsoft.AspNetCore.SignalR;

namespace JulOS.Server.Events;

/// <summary>Authenticated transport endpoint for small control-plane change notifications.</summary>
internal sealed class JulOsEventHub : Hub
{
}

/// <summary>Publishes transport-neutral Application notifications through SignalR.</summary>
internal sealed class SignalRRealtimeEventPublisher : IRealtimeEventPublisher
{
    private const int MaximumEventTypeLength = 256;
    private const int MaximumCorrelationIdLength = 64;
    private const int MaximumResourceIdLength = 512;
    private const int MaximumPayloadBytes = 8192;

    private readonly IHubContext<JulOsEventHub> hubContext;
    private readonly TimeProvider timeProvider;

    public SignalRRealtimeEventPublisher(
        IHubContext<JulOsEventHub> hubContext,
        TimeProvider timeProvider)
    {
        this.hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public Task PublishAsync(
        RealtimeEventNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Validate(notification);

        var now = this.timeProvider.GetUtcNow();
        var envelope = new RealtimeEventEnvelope(
            Guid.CreateVersion7(now),
            notification.EventType,
            RealtimeEventContract.CurrentVersion,
            now,
            notification.CorrelationId,
            notification.ResourceId,
            notification.Revision,
            notification.Payload.Clone());

        return this.hubContext.Clients.All.SendAsync(
            RealtimeEventContract.ClientMethod,
            envelope,
            cancellationToken);
    }

    private static void Validate(RealtimeEventNotification notification)
    {
        ValidateSafeText(
            notification.EventType,
            MaximumEventTypeLength,
            nameof(notification.EventType));
        if (notification.EventType.Any(character =>
            !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("The real-time event type is invalid.", nameof(notification));
        }

        ValidateSafeText(
            notification.CorrelationId,
            MaximumCorrelationIdLength,
            nameof(notification.CorrelationId));
        if (notification.CorrelationId.Any(character =>
            !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException(
                "The real-time event correlation identifier is invalid.",
                nameof(notification));
        }

        ValidateSafeText(
            notification.ResourceId,
            MaximumResourceIdLength,
            nameof(notification.ResourceId));
        if (notification.Revision is < 1)
        {
            throw new ArgumentException("The real-time event revision is invalid.", nameof(notification));
        }

        if (Encoding.UTF8.GetByteCount(notification.Payload.GetRawText()) > MaximumPayloadBytes)
        {
            throw new ArgumentException("The real-time event payload is too large.", nameof(notification));
        }
    }

    private static void ValidateSafeText(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException($"The real-time event field '{name}' is invalid.", name);
        }
    }
}

/// <summary>Registers and maps the authenticated real-time event transport.</summary>
internal static class RealtimeEventServices
{
    internal static IServiceCollection AddJulOsRealtimeEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 16 * 1024;
        });
        services.AddSingleton<IRealtimeEventPublisher, SignalRRealtimeEventPublisher>();
        return services;
    }

    internal static IEndpointRouteBuilder MapJulOsRealtimeEvents(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHub<JulOsEventHub>(RealtimeEventContract.HubPath)
            .RequireAuthorization();
        return endpoints;
    }
}
