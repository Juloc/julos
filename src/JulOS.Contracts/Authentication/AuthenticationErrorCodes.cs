namespace JulOS.Contracts.Authentication;

/// <summary>Stable machine-readable failures owned by local authentication.</summary>
public static class AuthenticationErrorCodes
{
    /// <summary>The one-time administrator setup has already completed.</summary>
    public const string SetupAlreadyCompleted = "authentication.setup_already_completed";

    /// <summary>Local authentication cannot be used until the initial administrator exists.</summary>
    public const string SetupRequired = "authentication.setup_required";

    /// <summary>The submitted account details do not satisfy the local account policy.</summary>
    public const string InvalidSetupRequest = "authentication.invalid_setup_request";

    /// <summary>The supplied username or password did not authenticate an account.</summary>
    public const string InvalidCredentials = "authentication.invalid_credentials";

    /// <summary>The mutation did not carry a valid antiforgery token.</summary>
    public const string AntiforgeryInvalid = "authentication.antiforgery_invalid";
}
