using JulOS.Contracts.Authentication;

namespace JulOS.Application.Authentication;

/// <summary>The local-authentication failure categories understood by the Server boundary.</summary>
public enum AuthenticationFailureReason
{
    /// <summary>The one-time setup has already completed.</summary>
    SetupAlreadyCompleted,

    /// <summary>The one-time setup must complete before local sign-in is possible.</summary>
    SetupRequired,

    /// <summary>The initial account request does not satisfy the configured policy.</summary>
    InvalidSetupRequest,

    /// <summary>The submitted credentials did not authenticate an account.</summary>
    InvalidCredentials,

    /// <summary>The cookie-authenticated mutation did not carry a valid antiforgery token.</summary>
    AntiforgeryInvalid,
}

/// <summary>
/// A safe, typed local-authentication refusal.
/// </summary>
/// <remarks>
/// The exception exposes only a deliberate client detail. Identity provider errors,
/// password hashes and credential values never cross this boundary.
/// </remarks>
public sealed class AuthenticationFailureException : Exception
{
    /// <summary>Initializes a local-authentication refusal.</summary>
    /// <param name="reason">The stable category used by the HTTP boundary.</param>
    /// <param name="innerException">The optional server-side cause.</param>
    public AuthenticationFailureException(
        AuthenticationFailureReason reason,
        Exception? innerException = null)
        : base(DetailFor(reason), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>Gets the stable refusal category.</summary>
    public AuthenticationFailureReason Reason { get; }

    /// <summary>Gets the stable public error code.</summary>
    public string Code => this.Reason switch
    {
        AuthenticationFailureReason.SetupAlreadyCompleted => AuthenticationErrorCodes.SetupAlreadyCompleted,
        AuthenticationFailureReason.SetupRequired => AuthenticationErrorCodes.SetupRequired,
        AuthenticationFailureReason.InvalidSetupRequest => AuthenticationErrorCodes.InvalidSetupRequest,
        AuthenticationFailureReason.InvalidCredentials => AuthenticationErrorCodes.InvalidCredentials,
        AuthenticationFailureReason.AntiforgeryInvalid => AuthenticationErrorCodes.AntiforgeryInvalid,
        _ => throw new InvalidOperationException($"Unknown authentication failure reason '{this.Reason}'."),
    };

    private static string DetailFor(AuthenticationFailureReason reason)
    {
        return reason switch
        {
            AuthenticationFailureReason.SetupAlreadyCompleted =>
                "The initial administrator has already been created.",
            AuthenticationFailureReason.SetupRequired =>
                "Create the initial administrator before signing in.",
            AuthenticationFailureReason.InvalidSetupRequest =>
                "The account details do not satisfy the local authentication policy.",
            AuthenticationFailureReason.InvalidCredentials =>
                "The username or password is invalid.",
            AuthenticationFailureReason.AntiforgeryInvalid =>
                "The antiforgery token is missing or invalid.",
            _ => throw new InvalidOperationException($"Unknown authentication failure reason '{reason}'."),
        };
    }
}
