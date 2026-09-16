using System.Security.Claims;

using JulOS.Application.Devices;
using JulOS.Contracts.Devices;
using JulOS.Server.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Devices;

/// <summary>
/// Maps the background-execution preference contract from <c>docs/MOBILE_PWA.md</c>
/// section 11.
/// </summary>
/// <remarks>
/// The preference belongs to the authenticated user and is reachable only through this
/// authenticated, antiforgery-protected surface. A package has no way to call it, which is
/// what makes "an application cannot enable keep-surface-active for itself" a property of
/// the system rather than an instruction packages are trusted to follow.
/// </remarks>
internal static class ApplicationExecutionPreferenceEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsApplicationExecutionPreferences(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/application-execution-preferences")
            .WithTags("ClientDevices")
            .RequireAuthorization();

        group.MapGet("/{applicationDefinitionId:guid}/current", ReadAsync);
        group.MapPut("/{applicationDefinitionId:guid}/current", WriteAsync).RequireJulOsAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context,
        Guid applicationDefinitionId,
        string workspaceClass,
        IApplicationExecutionPreferenceService preferences,
        CancellationToken cancellationToken)
    {
        var response = await preferences
            .ReadCurrentAsync(
                CurrentUserId(context.User),
                applicationDefinitionId,
                workspaceClass,
                ReadDeviceKey(context),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> WriteAsync(
        HttpContext context,
        Guid applicationDefinitionId,
        string workspaceClass,
        UpdateApplicationExecutionPreferenceRequest request,
        IAntiforgery antiforgery,
        IApplicationExecutionPreferenceService preferences,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var result = await preferences
            .WriteCurrentAsync(
                CurrentUserId(context.User),
                applicationDefinitionId,
                workspaceClass,
                ReadDeviceKey(context),
                request,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Created
            ? TypedResults.Created(
                $"/api/v1/application-execution-preferences/{applicationDefinitionId}/current",
                result.Preference)
            : TypedResults.Ok(result.Preference);
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
            : throw new ApplicationExecutionPreferenceException(
                ClientDeviceErrorCodes.NotOwned,
                "The request is not associated with an authenticated user.");
    }
}
