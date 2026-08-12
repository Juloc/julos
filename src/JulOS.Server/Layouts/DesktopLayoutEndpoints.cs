using System.Security.Claims;

using JulOS.Application.Layouts;
using JulOS.Contracts.Layouts;
using JulOS.Server.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Layouts;

/// <summary>Maps per-user, per-viewport desktop layout persistence.</summary>
internal static class DesktopLayoutEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsDesktopLayouts(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/desktop/layouts")
            .WithTags("Desktop")
            .RequireAuthorization();

        group.MapGet("/{viewport}", ReadAsync);
        group.MapPut("/{viewport}", SaveAsync).RequireJulOsAntiforgery();
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context,
        string viewport,
        IDesktopLayoutService service,
        CancellationToken cancellationToken)
    {
        var response = await service.ReadAsync(
            CurrentUserId(context.User),
            viewport,
            cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext context,
        string viewport,
        SaveDesktopLayoutRequest request,
        IAntiforgery antiforgery,
        IDesktopLayoutService service,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        try
        {
            var response = await service.SaveAsync(
                CurrentUserId(context.User),
                viewport,
                request,
                cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(response);
        }
        catch (ArgumentException)
        {
            // A malformed layout document is a caller error, not a server fault: answer 400 rather than
            // letting the exception surface as an unhandled 500.
            return Results.Json(
                new { code = "desktop.layout_invalid", detail = "The desktop layout document is invalid." },
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new InvalidOperationException("The authenticated principal has no valid user identifier.");
    }
}
