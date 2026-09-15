using System.Security.Claims;

using JulOS.Application.Devices;
using JulOS.Contracts.Devices;
using JulOS.Server.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Devices;

/// <summary>Maps the client-device HTTP contract from <c>docs/MOBILE_PWA.md</c> section 3.</summary>
/// <remarks>
/// Every endpoint requires an authenticated user. The device cookie selects which of that
/// user's devices is addressed and is never an authorization input on its own.
/// </remarks>
internal static class ClientDeviceEndpoints
{
    /// <summary>Name of the cookie carrying the client instance key.</summary>
    internal const string CookieName = ".JulOS.Device";

    /// <summary>How long a device cookie survives without contact.</summary>
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(400);

    internal static IEndpointRouteBuilder MapJulOsClientDevices(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/client-devices")
            .WithTags("ClientDevices")
            .RequireAuthorization();

        group.MapGet(string.Empty, ListAsync);
        group.MapPost("/registration", RegisterAsync).RequireJulOsAntiforgery();
        group.MapPut("/{clientDeviceId:guid}", UpdateAsync).RequireJulOsAntiforgery();
        group.MapPut("/{clientDeviceId:guid}/preferences/{workspaceClass}", SetPreferenceAsync)
            .RequireJulOsAntiforgery();
        group.MapDelete("/{clientDeviceId:guid}", RemoveAsync).RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IClientDeviceService devices,
        CancellationToken cancellationToken)
    {
        var result = await devices
            .ListAsync(CurrentUserId(context.User), ReadKey(context), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> RegisterAsync(
        HttpContext context,
        RegisterClientDeviceRequest request,
        IAntiforgery antiforgery,
        IClientDeviceService devices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var registration = await devices
            .RegisterOrResolveAsync(CurrentUserId(context.User), ReadKey(context), request, cancellationToken)
            .ConfigureAwait(false);

        if (registration.IssuedKey is not null)
        {
            WriteKey(context, registration.IssuedKey);
        }

        // The issued key is set as a cookie and is deliberately absent from the body, so
        // it never reaches Desktop JavaScript or a log of the response.
        return registration.IssuedKey is null
            ? TypedResults.Ok(registration.Device)
            : TypedResults.Created($"/api/v1/client-devices/{registration.Device.ClientDeviceId}", registration.Device);
    }

    private static async Task<IResult> UpdateAsync(
        HttpContext context,
        Guid clientDeviceId,
        UpdateClientDeviceRequest request,
        IAntiforgery antiforgery,
        IClientDeviceService devices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var device = await devices
            .UpdateAsync(CurrentUserId(context.User), clientDeviceId, request, ReadKey(context), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(device);
    }

    private static async Task<IResult> SetPreferenceAsync(
        HttpContext context,
        Guid clientDeviceId,
        string workspaceClass,
        UpdateDeviceWorkspacePreferenceRequest request,
        IAntiforgery antiforgery,
        IClientDeviceService devices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var device = await devices
            .SetPreferenceAsync(
                CurrentUserId(context.User),
                clientDeviceId,
                workspaceClass,
                request,
                ReadKey(context),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(device);
    }

    private static async Task<IResult> RemoveAsync(
        HttpContext context,
        Guid clientDeviceId,
        int revision,
        IAntiforgery antiforgery,
        IClientDeviceService devices,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        // A missing or unparsable value is already rejected as request.invalid by parameter
        // binding; this covers the values that parse but cannot identify a stored revision,
        // so every unusable revision reports the one documented code.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);

        var userId = CurrentUserId(context.User);
        var removingCurrent = await IsCurrentDeviceAsync(devices, userId, context, clientDeviceId, cancellationToken)
            .ConfigureAwait(false);

        await devices
            .RemoveAsync(userId, clientDeviceId, revision, cancellationToken)
            .ConfigureAwait(false);

        // Removing the device you are using must not leave a cookie that resolves to
        // nothing; clearing it makes the next request register a visibly new device.
        if (removingCurrent)
        {
            context.Response.Cookies.Delete(CookieName, BuildCookieOptions(context, TimeSpan.Zero));
        }

        return TypedResults.NoContent();
    }

    private static async Task<bool> IsCurrentDeviceAsync(
        IClientDeviceService devices,
        Guid userId,
        HttpContext context,
        Guid clientDeviceId,
        CancellationToken cancellationToken)
    {
        var known = await devices.ListAsync(userId, ReadKey(context), cancellationToken).ConfigureAwait(false);
        return known.Any(device => device.ClientDeviceId == clientDeviceId && device.IsCurrentDevice);
    }

    private static string? ReadKey(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var key) && !string.IsNullOrWhiteSpace(key)
            ? key
            : null;

    private static void WriteKey(HttpContext context, string key) =>
        context.Response.Cookies.Append(CookieName, key, BuildCookieOptions(context, CookieLifetime));

    private static CookieOptions BuildCookieOptions(HttpContext context, TimeSpan lifetime) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        // SameAsRequest rather than Always, for the reason recorded in decision D027: the
        // supported single-container deployment binds Kestrel to plain loopback HTTP, and
        // an Always policy makes the browser drop the cookie there.
        Secure = context.Request.IsHttps,
        MaxAge = lifetime == TimeSpan.Zero ? TimeSpan.Zero : lifetime,
    };

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new ClientDeviceException(
                ClientDeviceErrorCodes.NotOwned,
                "The request is not associated with an authenticated user.");
    }
}
