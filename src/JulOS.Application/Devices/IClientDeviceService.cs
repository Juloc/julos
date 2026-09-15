using JulOS.Contracts.Devices;

namespace JulOS.Application.Devices;

/// <summary>
/// Registers and reads the authenticated user's client devices and their layout
/// preferences.
/// </summary>
/// <remarks>
/// Every method takes the authenticated user explicitly. The device key is never an
/// authorization input: it selects which of that user's devices is being addressed, and a
/// key belonging to another user resolves to no device rather than to theirs.
/// </remarks>
public interface IClientDeviceService
{
    /// <summary>
    /// Resolves the device for a presented key, or registers a new one when the key is
    /// absent or unknown.
    /// </summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="presentedKey">The raw key from the device cookie, if the request carried one.</param>
    /// <param name="request">What the client reports about itself.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    /// <returns>
    /// The resolved device and, when a new device was registered, the raw key the caller
    /// must set as a cookie. The raw key is returned exactly once and never stored.
    /// </returns>
    Task<ClientDeviceRegistration> RegisterOrResolveAsync(
        Guid userId,
        string? presentedKey,
        RegisterClientDeviceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the user's devices.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="presentedKey">Key of the calling device, used only to mark which entry is current.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task<IReadOnlyList<ClientDeviceResponse>> ListAsync(
        Guid userId,
        string? presentedKey,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a device or changes its workspace pin.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="clientDeviceId">Device to change.</param>
    /// <param name="request">New name, pin and expected revision.</param>
    /// <param name="presentedKey">Key of the calling device, used only to report whether the result is current.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task<ClientDeviceResponse> UpdateAsync(
        Guid userId,
        Guid clientDeviceId,
        UpdateClientDeviceRequest request,
        string? presentedKey,
        CancellationToken cancellationToken = default);

    /// <summary>Sets one device's preference for a single workspace class.</summary>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="clientDeviceId">Device to change.</param>
    /// <param name="workspaceClass">Workspace class the preference applies to.</param>
    /// <param name="request">Scope, restore mode and expected revision.</param>
    /// <param name="presentedKey">Key of the calling device, used only to report whether the result is current.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task<ClientDeviceResponse> SetPreferenceAsync(
        Guid userId,
        Guid clientDeviceId,
        string workspaceClass,
        UpdateDeviceWorkspacePreferenceRequest request,
        string? presentedKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a device and only that device's preferences.
    /// </summary>
    /// <remarks>
    /// Shared layouts and application data are never touched: removing a device is a
    /// presentation-preference action, not a data-deletion action.
    /// </remarks>
    /// <param name="userId">Authenticated user.</param>
    /// <param name="clientDeviceId">Device to remove.</param>
    /// <param name="expectedRevision">The revision the caller based the removal on.</param>
    /// <param name="cancellationToken">Operation cancellation.</param>
    Task RemoveAsync(
        Guid userId,
        Guid clientDeviceId,
        int expectedRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of resolving or registering a client device.</summary>
/// <param name="Device">The resolved device.</param>
/// <param name="IssuedKey">
/// The raw client instance key when a new device was registered, otherwise null. It is
/// returned once, set as a cookie by the caller and never persisted in clear form.
/// </param>
public sealed record ClientDeviceRegistration(ClientDeviceResponse Device, string? IssuedKey);

/// <summary>A refused client-device operation carrying a stable code.</summary>
public sealed class ClientDeviceException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="code">A stable code from <see cref="ClientDeviceErrorCodes"/>.</param>
    /// <param name="message">Caller-safe explanation.</param>
    public ClientDeviceException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        this.Code = code;
    }

    /// <summary>The stable failure code.</summary>
    public string Code { get; }
}
