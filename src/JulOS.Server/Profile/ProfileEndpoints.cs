using System.Security.Claims;

using JulOS.Application.Profile;
using JulOS.Contracts.Profile;
using JulOS.Server.Authentication;

using Microsoft.AspNetCore.Antiforgery;

namespace JulOS.Server.Profile;

/// <summary>Maps the authenticated profile HTTP contract.</summary>
internal static class ProfileEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsProfile(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        group.MapGet(string.Empty, ReadAsync);
        group.MapPut("/preferences", UpdatePreferencesAsync)
            .RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context,
        IProfileService profiles,
        CancellationToken cancellationToken)
    {
        var profile = await profiles
            .ReadAsync(CurrentUserId(context.User), cancellationToken)
            .ConfigureAwait(false);
        return TypedResults.Ok(ToResponse(profile));
    }

    private static async Task<IResult> UpdatePreferencesAsync(
        HttpContext context,
        UpdateProfilePreferencesRequest request,
        IAntiforgery antiforgery,
        IProfileService profiles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        var profile = await profiles.UpdatePreferencesAsync(
            CurrentUserId(context.User),
            request.PreferredLanguage,
            request.TimeZone,
            request.Theme,
            request.Motion,
            request.Revision,
            cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(profile));
    }

    private static ProfileResponse ToResponse(UserProfile profile) => new(
        profile.UserId,
        profile.UserName,
        profile.DisplayName,
        profile.PreferredLanguage,
        profile.TimeZone,
        profile.Theme,
        profile.Motion,
        profile.Revision);

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : throw new ProfileFailureException(ProfileFailureReason.NotFound);
    }
}
