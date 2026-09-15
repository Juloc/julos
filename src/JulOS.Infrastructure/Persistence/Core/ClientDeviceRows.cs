using JulOS.Domain.Devices;
using JulOS.Domain.Primitives;

namespace JulOS.Infrastructure.Persistence.Core;

/// <summary>Persistence shape of one registered client device.</summary>
/// <remarks>
/// Only the hash of the client instance key is stored. The key itself exists in the
/// device cookie and nowhere else, so a database copy cannot be replayed as a device.
/// </remarks>
internal sealed class ClientDeviceRow
{
    internal Guid Id { get; set; }

    internal Guid OwnerUserId { get; set; }

    internal required string ClientInstanceKeyHash { get; set; }

    internal required string DisplayName { get; set; }

    internal WorkspaceClass LastDetectedWorkspaceClass { get; set; }

    internal WorkspaceClass? WorkspaceClassOverride { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset LastSeenAtUtc { get; set; }

    internal int Revision { get; set; }

    internal List<DeviceWorkspacePreferenceRow> Preferences { get; } = [];

    internal static ClientDeviceRow FromDomain(ClientDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var row = new ClientDeviceRow
        {
            Id = device.Id.Value,
            OwnerUserId = device.OwnerUserId,
            ClientInstanceKeyHash = device.ClientInstanceKeyHash,
            DisplayName = device.DisplayName,
            LastDetectedWorkspaceClass = device.LastDetectedWorkspaceClass,
            WorkspaceClassOverride = device.WorkspaceClassOverride,
            CreatedAtUtc = device.CreatedAtUtc,
            LastSeenAtUtc = device.LastSeenAtUtc,
            Revision = device.Revision.Value,
        };

        foreach (var preference in device.Preferences)
        {
            row.Preferences.Add(DeviceWorkspacePreferenceRow.FromDomain(device.Id, preference));
        }

        return row;
    }

    internal ClientDevice ToDomain() => ClientDevice.Restore(
        new ClientDeviceId(this.Id),
        this.OwnerUserId,
        this.ClientInstanceKeyHash,
        this.DisplayName,
        this.LastDetectedWorkspaceClass,
        this.WorkspaceClassOverride,
        this.CreatedAtUtc,
        this.LastSeenAtUtc,
        Domain.Primitives.Revision.From(this.Revision),
        this.Preferences.Select(preference => preference.ToDomain()));
}

/// <summary>Persistence shape of one device's preference for a single workspace class.</summary>
internal sealed class DeviceWorkspacePreferenceRow
{
    internal Guid ClientDeviceId { get; set; }

    internal WorkspaceClass WorkspaceClass { get; set; }

    internal LayoutScope LayoutScope { get; set; }

    internal RestoreMode RestoreMode { get; set; }

    internal static DeviceWorkspacePreferenceRow FromDomain(
        ClientDeviceId deviceId,
        DeviceWorkspacePreference preference) =>
        new()
        {
            ClientDeviceId = deviceId.Value,
            WorkspaceClass = preference.WorkspaceClass,
            LayoutScope = preference.Scope,
            RestoreMode = preference.RestoreMode,
        };

    internal DeviceWorkspacePreference ToDomain() =>
        new(this.WorkspaceClass, this.LayoutScope, this.RestoreMode);
}

/// <summary>Persistence shape of one application's background-execution preference.</summary>
internal sealed class ApplicationExecutionPreferenceRow
{
    internal Guid Id { get; set; }

    internal Guid OwnerUserId { get; set; }

    internal Guid ApplicationDefinitionId { get; set; }

    internal WorkspaceClass WorkspaceClass { get; set; }

    /// <summary>Null is the user's shared preference; a value scopes it to one device.</summary>
    internal Guid? ClientDeviceId { get; set; }

    internal BackgroundMode BackgroundMode { get; set; }

    internal int Revision { get; set; }
}
