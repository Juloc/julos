using System.Security.Claims;

using JulOS.Application.Layouts;
using JulOS.Contracts.Devices;
using JulOS.Contracts.Layouts;
using JulOS.Server.Authentication;
using JulOS.Server.Devices;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Layouts;

/// <summary>Maps the workspace-layout contract from <c>docs/MOBILE_PWA.md</c> section 15.</summary>
/// <remarks>
/// The route names a workspace class, never a layout identity or a scope. Whether the
/// shared or the device layout answers is resolved on the server from the authenticated
/// user and the owner-scoped device cookie, so a caller cannot reach another user's
/// layout, or their own device layout, through request data.
/// </remarks>
internal static class WorkspaceLayoutEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsWorkspaceLayouts(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/workspace-layouts")
            .WithTags("Desktop")
            .RequireAuthorization();

        group.MapGet("/{workspaceClass}/current", ReadAsync);
        group.MapPut("/{workspaceClass}/current", WriteAsync).RequireJulOsAntiforgery();
        group.MapPost($"/{WorkspaceClassNames.DesktopMulti}/initialization", InitializeMultiDisplayAsync)
            .RequireJulOsAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context,
        string workspaceClass,
        IWorkspaceLayoutService service,
        CancellationToken cancellationToken)
    {
        var response = await service
            .ReadCurrentAsync(CurrentUserId(context.User), workspaceClass, ReadDeviceKey(context), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> WriteAsync(
        HttpContext context,
        string workspaceClass,
        WorkspaceLayoutWriteRequest request,
        IAntiforgery antiforgery,
        IWorkspaceLayoutService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var result = await service
            .WriteCurrentAsync(
                CurrentUserId(context.User),
                workspaceClass,
                ReadDeviceKey(context),
                request,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Created
            ? TypedResults.Created($"/api/v1/workspace-layouts/{workspaceClass}/current", result.Layout)
            : TypedResults.Ok(result.Layout);
    }

    private static async Task<IResult> InitializeMultiDisplayAsync(
        HttpContext context,
        InitializeMultiDisplayRequest request,
        IAntiforgery antiforgery,
        IWorkspaceLayoutService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var result = await service
            .InitializeMultiDisplayAsync(
                CurrentUserId(context.User),
                ReadDeviceKey(context),
                request.CopyFromDesktopSingle,
                request.DisplayCount,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Created
            ? TypedResults.Created(
                $"/api/v1/workspace-layouts/{WorkspaceClassNames.DesktopMulti}/current",
                result.Layout)
            : TypedResults.Ok(result.Layout);
    }

    private static string? ReadDeviceKey(HttpContext context) =>
        context.Request.Cookies.TryGetValue(ClientDeviceEndpoints.CookieName, out var key)
        && !string.IsNullOrWhiteSpace(key)
            ? key
            : null;

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new WorkspaceLayoutFailureException(
                WorkspaceLayoutFailureReason.InvalidLayout,
                "The request is not associated with an authenticated user.");
    }
}

/// <summary>Explicitly initializes the multi-display layout.</summary>
/// <param name="CopyFromDesktopSingle">Copy the single-display arrangement instead of starting empty.</param>
/// <param name="DisplayCount">How many display participants the new layout is arranged for.</param>
/// <remarks>
/// Multi-display is a decision, not a detection. Nothing creates this layout implicitly,
/// because a physical display topology cannot be inferred from stored window geometry.
/// </remarks>
public sealed record InitializeMultiDisplayRequest(bool CopyFromDesktopSingle, int DisplayCount);
