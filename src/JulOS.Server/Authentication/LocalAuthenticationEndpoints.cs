using System.Security.Claims;

using JulOS.Application.Auditing;
using JulOS.Application.Authentication;
using JulOS.Contracts.Authentication;
using JulOS.Domain.Observability;
using JulOS.Infrastructure.Authentication;
using JulOS.Server.Errors;

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
        SignInManager<LocalUser> signInManager,
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
        if (user is not null)
        {
            // Desktop boot calls /auth/status. Reissue the existing persistent cookie here so
            // opening JulOS renews the full configured lifetime without binding auth to a
            // desktop/mobile presentation mode or user-agent string.
            await signInManager.RefreshSignInAsync(user).ConfigureAwait(false);
        }

        return TypedResults.Ok(
            new AuthenticationStatusResponse(
                setupRequired,
                user is not null,
                user is null ? null : ToResponse(user)));
    }

    private static async Task<IResult> SetupAsync(
        HttpContext context,
        InitialAdministratorRequest request,
        InitialAdministratorProvisioner provisioner,
        SignInManager<LocalUser> signInManager,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var user = await provisioner
                .CreateAsync(
                    request.UserName,
                    request.DisplayName,
                    request.Password,
                    cancellationToken)
                .ConfigureAwait(false);

            await AppendAuthenticationAuditAsync(
                context,
                auditService,
                user.Id,
                "authentication.setup",
                "user",
                user.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                AuditOutcome.Succeeded,
                "Initial administrator created.",
                cancellationToken).ConfigureAwait(false);

            await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);

            var response = ToResponse(user);
            return TypedResults.Created("/api/v1/auth/status", response);
        }
        catch (AuthenticationFailureException)
        {
            await AppendAuthenticationAuditAsync(
                context,
                auditService,
                userId: null,
                "authentication.setup",
                "authentication_setup",
                "initial",
                AuditOutcome.Denied,
                "Initial administrator setup denied.",
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        LocalLoginRequest request,
        InitialAdministratorProvisioner provisioner,
        UserManager<LocalUser> userManager,
        SignInManager<LocalUser> signInManager,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await provisioner.IsSetupRequiredAsync(cancellationToken).ConfigureAwait(false))
        {
            await AppendAuthenticationAuditAsync(
                context,
                auditService,
                userId: null,
                "authentication.login",
                "user",
                "unknown",
                AuditOutcome.Denied,
                "Login denied.",
                cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.SetupRequired);
        }

        if (string.IsNullOrWhiteSpace(request.UserName)
            || string.IsNullOrEmpty(request.Password)
            || request.UserName.Length > 128
            || request.Password.Length > 1024)
        {
            await AppendAuthenticationAuditAsync(
                context,
                auditService,
                userId: null,
                "authentication.login",
                "user",
                "unknown",
                AuditOutcome.Denied,
                "Login denied.",
                cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidCredentials);
        }

        var user = await userManager
            .FindByNameAsync(request.UserName)
            .ConfigureAwait(false);

        if (user is null)
        {
            await AppendAuthenticationAuditAsync(
                context,
                auditService,
                userId: null,
                "authentication.login",
                "user",
                "unknown",
                AuditOutcome.Denied,
                "Login denied.",
                cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidCredentials);
        }

        var result = await signInManager
            .CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            await AppendAuthenticationAuditAsync(
                context,
                auditService,
                user.Id,
                "authentication.login",
                "user",
                user.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
                AuditOutcome.Denied,
                "Login denied.",
                cancellationToken).ConfigureAwait(false);
            throw new AuthenticationFailureException(
                AuthenticationFailureReason.InvalidCredentials);
        }

        await AppendAuthenticationAuditAsync(
            context,
            auditService,
            user.Id,
            "authentication.login",
            "user",
            user.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            AuditOutcome.Succeeded,
            "Login succeeded.",
            cancellationToken).ConfigureAwait(false);
        await signInManager.SignInAsync(user, isPersistent: true).ConfigureAwait(false);

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
        SignInManager<LocalUser> signInManager,
        IAuditService auditService,
        CancellationToken cancellationToken)
    {
        await JulOsAntiforgery.ValidateAsync(context, antiforgery).ConfigureAwait(false);
        var userId = CurrentUserId(context.User)
            ?? throw new InvalidOperationException("The authenticated principal has no valid user identifier.");

        await AppendAuthenticationAuditAsync(
            context,
            auditService,
            userId,
            "authentication.logout",
            "user",
            userId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            AuditOutcome.Succeeded,
            "Logout succeeded.",
            cancellationToken).ConfigureAwait(false);
        await signInManager.SignOutAsync().ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static Task AppendAuthenticationAuditAsync(
        HttpContext context,
        IAuditService auditService,
        Guid? userId,
        string action,
        string targetType,
        string targetId,
        AuditOutcome outcome,
        string summary,
        CancellationToken cancellationToken) => auditService.AppendAsync(
            new AuditRecord(
                userId,
                AgentId: null,
                SourcePackageId: null,
                action,
                targetType,
                targetId,
                outcome,
                CorrelationId.Get(context),
                context.Connection.RemoteIpAddress?.ToString(),
                summary,
                "Credential values omitted."),
            cancellationToken);

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var identifier = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(identifier, out var userId) && userId != Guid.Empty
            ? userId
            : null;
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
