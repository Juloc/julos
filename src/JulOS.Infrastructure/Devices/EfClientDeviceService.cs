using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using JulOS.Application.Concurrency;
using JulOS.Application.Devices;
using JulOS.Application.Events;
using JulOS.Contracts.Devices;
using JulOS.Domain.Devices;
using JulOS.Domain.Primitives;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Devices;

/// <summary>Stores the authenticated user's client devices and their layout preferences.</summary>
/// <remarks>
/// <para>
/// Every query is filtered by the authenticated user as well as by the device key or
/// identity. A key or identity belonging to another user therefore resolves to nothing
/// rather than to that user's device, so the device cookie can never be used to read or
/// adopt someone else's device.
/// </para>
/// <para>
/// The raw key is generated here, returned once for the caller to set as a cookie and
/// never stored: only its SHA-256 hash is persisted.
/// </para>
/// </remarks>
internal sealed class EfClientDeviceService : IClientDeviceService
{
    /// <summary>Length of the generated client instance key. 256 bits, per section 3.</summary>
    private const int KeyByteLength = 32;

    /// <summary>A user cannot register an unbounded number of devices.</summary>
    private const int MaximumDevicesPerUser = 64;

    private readonly CoreDbContext context;
    private readonly IIdentifierGenerator identifiers;
    private readonly TimeProvider timeProvider;
    private readonly IRealtimeEventPublisher? events;

    public EfClientDeviceService(
        CoreDbContext context,
        IIdentifierGenerator identifiers,
        TimeProvider timeProvider,
        IRealtimeEventPublisher? events = null)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.identifiers = identifiers ?? throw new ArgumentNullException(nameof(identifiers));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.events = events;
    }

    /// <summary>Announces that a device or its preferences changed.</summary>
    /// <remarks>
    /// The payload carries only the device identity: the HTTP API stays authoritative, and
    /// no device key, hash or preference value is ever placed on the event bus.
    /// </remarks>
    private async Task PublishChangedAsync(
        Guid clientDeviceId,
        int? revision,
        CancellationToken cancellationToken)
    {
        if (this.events is null)
        {
            return;
        }

        await this.events.PublishAsync(
            new RealtimeEventNotification(
                ClientDeviceEvents.Changed,
                clientDeviceId.ToString(),
                clientDeviceId.ToString(),
                revision,
                EmptyPayload),
            cancellationToken).ConfigureAwait(false);
    }

    private static readonly JsonElement EmptyPayload = JsonDocument.Parse("{}").RootElement;

    public async Task<ClientDeviceRegistration> RegisterOrResolveAsync(
        Guid userId,
        string? presentedKey,
        RegisterClientDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUser(userId);

        var detected = ParseWorkspaceClass(request.DetectedWorkspaceClass);
        var now = this.timeProvider.GetUtcNow();
        var existing = await this.FindByKeyAsync(userId, presentedKey, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            var device = existing.ToDomain();
            device.RecordDetectedWorkspace(detected, now);
            existing.LastDetectedWorkspaceClass = device.LastDetectedWorkspaceClass;
            existing.LastSeenAtUtc = device.LastSeenAtUtc;
            await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new ClientDeviceRegistration(ToResponse(existing, isCurrent: true), IssuedKey: null);
        }

        var deviceCount = await this.context.ClientDevices
            .CountAsync(row => row.OwnerUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (deviceCount >= MaximumDevicesPerUser)
        {
            throw new ClientDeviceException(
                ClientDeviceErrorCodes.Invalid,
                $"A user may register at most {MaximumDevicesPerUser} devices. Remove one in Settings.");
        }

        var key = GenerateKey();
        var registered = ClientDevice.Register(
            new ClientDeviceId(this.identifiers.Create()),
            userId,
            HashKey(key),
            request.DisplayName,
            detected,
            now);

        var row = ClientDeviceRow.FromDomain(registered);
        _ = this.context.ClientDevices.Add(row);
        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await this.PublishChangedAsync(row.Id, row.Revision, cancellationToken).ConfigureAwait(false);
        return new ClientDeviceRegistration(ToResponse(row, isCurrent: true), key);
    }

    public async Task<IReadOnlyList<ClientDeviceResponse>> ListAsync(
        Guid userId,
        string? presentedKey,
        CancellationToken cancellationToken = default)
    {
        RequireUser(userId);

        var currentHash = presentedKey is null ? null : HashKey(presentedKey);
        var rows = await this.context.ClientDevices
            .AsNoTracking()
            .Include(row => row.Preferences)
            .Where(row => row.OwnerUserId == userId)
            .OrderBy(row => row.CreatedAtUtc)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => ToResponse(
                row,
                currentHash is not null
                    && string.Equals(row.ClientInstanceKeyHash, currentHash, StringComparison.Ordinal)))
            .ToArray();
    }

    public async Task<ClientDeviceResponse> UpdateAsync(
        Guid userId,
        Guid clientDeviceId,
        UpdateClientDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUser(userId);

        var row = await this.RequireOwnedAsync(userId, clientDeviceId, cancellationToken).ConfigureAwait(false);
        RequireRevision(row, request.ExpectedRevision);

        var device = row.ToDomain();
        device.Rename(request.DisplayName);
        device.OverrideWorkspace(
            request.WorkspaceClassOverride is null ? null : ParseWorkspaceClass(request.WorkspaceClassOverride));

        row.DisplayName = device.DisplayName;
        row.WorkspaceClassOverride = device.WorkspaceClassOverride;
        row.Revision = device.Revision.Value;

        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(row.Id, row.Revision, cancellationToken).ConfigureAwait(false);
        return ToResponse(row, isCurrent: false);
    }

    public async Task<ClientDeviceResponse> SetPreferenceAsync(
        Guid userId,
        Guid clientDeviceId,
        string workspaceClass,
        UpdateDeviceWorkspacePreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUser(userId);

        var row = await this.RequireOwnedAsync(userId, clientDeviceId, cancellationToken).ConfigureAwait(false);
        RequireRevision(row, request.ExpectedRevision);

        var parsed = ParseWorkspaceClass(workspaceClass, allowMultiDisplay: true);
        var device = row.ToDomain();
        device.SetPreference(parsed, ParseScope(request.LayoutScope), ParseRestoreMode(request.RestoreMode));

        var preference = row.Preferences.SingleOrDefault(entry => entry.WorkspaceClass == parsed);
        if (preference is null)
        {
            row.Preferences.Add(DeviceWorkspacePreferenceRow.FromDomain(
                new ClientDeviceId(row.Id),
                device.Preferences.Single(entry => entry.WorkspaceClass == parsed)));
        }
        else
        {
            preference.LayoutScope = ParseScope(request.LayoutScope);
            preference.RestoreMode = ParseRestoreMode(request.RestoreMode);
        }

        row.Revision = device.Revision.Value;
        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(row.Id, row.Revision, cancellationToken).ConfigureAwait(false);
        return ToResponse(row, isCurrent: false);
    }

    public async Task RemoveAsync(
        Guid userId,
        Guid clientDeviceId,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        RequireUser(userId);

        var row = await this.RequireOwnedAsync(userId, clientDeviceId, cancellationToken).ConfigureAwait(false);
        RequireRevision(row, expectedRevision);

        // Cascades remove this device's workspace preferences and its device-scoped
        // execution preferences. Shared layouts and application data are untouched.
        _ = this.context.ClientDevices.Remove(row);
        _ = await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await this.PublishChangedAsync(clientDeviceId, revision: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientDeviceRow?> FindByKeyAsync(
        Guid userId,
        string? presentedKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            return null;
        }

        var hash = HashKey(presentedKey);

        // Filtered by owner as well as by hash: a key that belongs to another user must
        // resolve to nothing rather than to that user's device.
        return await this.context.ClientDevices
            .Include(row => row.Preferences)
            .SingleOrDefaultAsync(
                row => row.OwnerUserId == userId && row.ClientInstanceKeyHash == hash,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ClientDeviceRow> RequireOwnedAsync(
        Guid userId,
        Guid clientDeviceId,
        CancellationToken cancellationToken)
    {
        var row = await this.context.ClientDevices
            .Include(entry => entry.Preferences)
            .SingleOrDefaultAsync(
                entry => entry.Id == clientDeviceId && entry.OwnerUserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        // A device owned by someone else is reported as not found, so the API never
        // confirms that another user's device exists.
        return row ?? throw new ClientDeviceException(
            ClientDeviceErrorCodes.NotFound,
            "The client device does not exist.");
    }

    private static void RequireRevision(ClientDeviceRow row, int expectedRevision)
    {
        if (row.Revision != expectedRevision)
        {
            throw new ConcurrencyConflictException(
                row.Revision,
                new InvalidOperationException("The client device changed after it was loaded."));
        }
    }

    private static void RequireUser(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ClientDeviceException(
                ClientDeviceErrorCodes.NotOwned,
                "A client device operation requires an authenticated user.");
        }
    }

    private static string GenerateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyByteLength))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string HashKey(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static ClientDeviceResponse ToResponse(ClientDeviceRow row, bool isCurrent) => new(
        row.Id,
        row.DisplayName,
        ToName(row.LastDetectedWorkspaceClass),
        row.WorkspaceClassOverride is null ? null : ToName(row.WorkspaceClassOverride.Value),
        row.CreatedAtUtc,
        row.LastSeenAtUtc,
        row.Revision,
        row.Preferences
            .OrderBy(preference => preference.WorkspaceClass)
            .Select(preference => new DeviceWorkspacePreferenceResponse(
                ToName(preference.WorkspaceClass),
                preference.LayoutScope == LayoutScope.Shared ? "shared" : "device",
                preference.RestoreMode == RestoreMode.Resume ? "resume" : "fresh"))
            .ToArray(),
        isCurrent);

    private static string ToName(WorkspaceClass workspaceClass) => workspaceClass switch
    {
        WorkspaceClass.Phone => WorkspaceClassNames.Phone,
        WorkspaceClass.Tablet => WorkspaceClassNames.Tablet,
        WorkspaceClass.DesktopSingle => WorkspaceClassNames.DesktopSingle,
        WorkspaceClass.DesktopMulti => WorkspaceClassNames.DesktopMulti,
        _ => throw new ClientDeviceException(
            ClientDeviceErrorCodes.WorkspaceClassInvalid,
            "The stored workspace class is not supported."),
    };

    private static WorkspaceClass ParseWorkspaceClass(string value, bool allowMultiDisplay = false) => value switch
    {
        WorkspaceClassNames.Phone => WorkspaceClass.Phone,
        WorkspaceClassNames.Tablet => WorkspaceClass.Tablet,
        WorkspaceClassNames.DesktopSingle => WorkspaceClass.DesktopSingle,
        WorkspaceClassNames.DesktopMulti when allowMultiDisplay => WorkspaceClass.DesktopMulti,
        WorkspaceClassNames.DesktopMulti => throw new ClientDeviceException(
            ClientDeviceErrorCodes.WorkspacePreferenceInvalid,
            "Multi-display is entered through the Multi-Display controller and cannot be pinned."),
        _ => throw new ClientDeviceException(
            ClientDeviceErrorCodes.WorkspaceClassInvalid,
            "The workspace class is not supported."),
    };

    private static LayoutScope ParseScope(string value) => value switch
    {
        "shared" => LayoutScope.Shared,
        "device" => LayoutScope.Device,
        _ => throw new ClientDeviceException(
            ClientDeviceErrorCodes.WorkspacePreferenceInvalid,
            "A layout scope is 'shared' or 'device'."),
    };

    private static RestoreMode ParseRestoreMode(string value) => value switch
    {
        "resume" => RestoreMode.Resume,
        "fresh" => RestoreMode.Fresh,
        _ => throw new ClientDeviceException(
            ClientDeviceErrorCodes.WorkspacePreferenceInvalid,
            "A restore mode is 'resume' or 'fresh'."),
    };
}
