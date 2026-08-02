namespace JulOS.Contracts.Authentication;

/// <summary>The request that creates the only initial local administrator.</summary>
/// <param name="UserName">The stable local sign-in name.</param>
/// <param name="DisplayName">The name shown in the JulOS interface.</param>
/// <param name="Password">The initial password, which is never returned or logged.</param>
public sealed record InitialAdministratorRequest(
    string UserName,
    string DisplayName,
    string Password);

/// <summary>A local username and password sign-in request.</summary>
/// <param name="UserName">The local sign-in name.</param>
/// <param name="Password">The account password.</param>
public sealed record LocalLoginRequest(
    string UserName,
    string Password);

/// <summary>The safe account information returned after authentication.</summary>
/// <param name="UserId">The stable JulOS user identifier.</param>
/// <param name="UserName">The stable local sign-in name.</param>
/// <param name="DisplayName">The name shown in the JulOS interface.</param>
public sealed record AuthenticatedUserResponse(
    Guid UserId,
    string UserName,
    string DisplayName);

/// <summary>The current local-authentication state.</summary>
/// <param name="SetupRequired">Whether the one-time administrator setup must still run.</param>
/// <param name="Authenticated">Whether the current request carries a valid session.</param>
/// <param name="User">The current account when authenticated.</param>
public sealed record AuthenticationStatusResponse(
    bool SetupRequired,
    bool Authenticated,
    AuthenticatedUserResponse? User);

/// <summary>An antiforgery token used by cookie-authenticated mutation requests.</summary>
/// <param name="HeaderName">The request header that must carry the token.</param>
/// <param name="Token">The request token paired with the secure antiforgery cookie.</param>
public sealed record AntiforgeryTokenResponse(
    string HeaderName,
    string Token);
