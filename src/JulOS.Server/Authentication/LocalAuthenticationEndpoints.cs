using JulOS.Application.Authentication;
using JulOS.Contracts.Authentication;
using JulOS.Infrastructure.Authentication;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace JulOS.Server.Authentication;

/// <summary>Maps the versioned local-authentication HTTP contract.</summary>
internal static class LocalAuthenticationEndpoints
{
    internal static IEndpointRouteBuilder MapJulOsLocalAuthentication(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        group.MapGet("/status", GetStatusAsync)
            .AllowAnonymous();

        group.MapPost("/setup", SetupAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LocalAuthenticationServices.LoginRateLimitPolicy);

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LocalAuthenticationServices.LoginRateLimitPolicy);

        group.MapGet("/antiforgery", GetAntiforgeryToken)
            .RequireAuthorization();

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .RequireJulOsAntiforgery();

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext context,
        InitialAdministratorProvisioner provisioner,
        UserManager<LocalUser> userManager,
        CancellationToken cancellationToken)
    {
        var setupRequired = await provisioner
            .IsSetupRequiredAsync(cancellationToken)
            .ConfigureAwait(false);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return TypedResults.Ok(
                new AuthenticationStatusResponse(
                    setupRequired,
                    Authenticated: false,
                    User: null));
        }

        var user = await userManager.GetUserAsync(context.User).ConfigureAwait(false);

        return TypedResults.Ok(
            new AuthenticationStatusResponse(
                setupRequired,
                user is not null,
                user is null ? null : ToResponse(user)));
    }

    private static async Task<IResult> SetupAsync(
        InitialAdministratorRequest request,
        InitialAdministratorProvisioner provisioner,
        SignInManager<LocalUser> signInManager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await provisioner
            .CreateAsync(
                request.UserName,
                request.DisplayName,
                request.Password,
                cancellationToken)
            .ConfigureAwait(false);

        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        var response = ToResponse(user);
        return TypedResults.Created("/api/v1/auth/status", response);
    }

    private static async Task<IResult> LoginAsync(
        LocalLoginRequest request,
        InitialAdministratorProvisioner provisioner,
        UserManager<LocalUser> userManager,
        SignInManager<LocalUser> signInManager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await provisioner.IsSetupRequiredAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.SetupRequired);
        }

        if (string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrEmpty(request.Password)
            || request.UserName.Length > 128
            || request.Password.Length > 1024)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidCredentials);
        }

        var user = await userManager
            .FindByNameAsync(request.UserName)
            .ConfigureAwait(false);

        if (user is null)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidCredentials);
        }

        var result = await signInManager
            .CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidCredentials);
        }

        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        return TypedResults.Ok(ToResponse(user));
    }

    private static Ok<AntiforgeryTokenResponse> GetAntiforgeryToken(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var requestToken = tokens.RequestToken
            ?? throw new InvalidOperationException("The antiforgery service did not create a request token.");
        var headerName = tokens.HeaderName
            ?? throw new InvalidOperationException("The antiforgery service did not expose its request header.");

        return TypedResults.Ok(new AntiforgeryTokenResponse(headerName, requestToken));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<LocalUser> signInManager)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);

        await signInManager.SignOutAsync().ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static AuthenticatedUserResponse ToResponse(LocalUser user)
    {
        return new AuthenticatedUserResponse(
            user.Id,
            user.UserName
                ?? throw new InvalidOperationException("A persisted local user has no username."),
            user.DisplayName);
    }
}
