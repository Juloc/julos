namespace JulOS.Contracts.Profile;

/// <summary>Stable language values accepted by the profile API.</summary>
public static class ProfileLanguages
{
    /// <summary>English user-interface language.</summary>
    public const string English = "en";

    /// <summary>German user-interface language.</summary>
    public const string German = "de";
}

/// <summary>Stable shell-theme values accepted by the profile API.</summary>
public static class ProfileThemes
{
    /// <summary>Follow the operating-system preference.</summary>
    public const string System = "system";

    /// <summary>Use the light shell theme.</summary>
    public const string Light = "light";

    /// <summary>Use the dark shell theme.</summary>
    public const string Dark = "dark";
}

/// <summary>Stable motion values accepted by the profile API.</summary>
public static class ProfileMotionPreferences
{
    /// <summary>Use normal state-transition motion.</summary>
    public const string Enabled = "enabled";

    /// <summary>Reduce non-essential state-transition motion.</summary>
    public const string Reduced = "reduced";
}

/// <summary>The authenticated user's profile and shell preferences.</summary>
/// <param name="UserId">The stable local user identifier.</param>
/// <param name="UserName">The local sign-in name.</param>
/// <param name="DisplayName">The name shown in the interface.</param>
/// <param name="PreferredLanguage">The selected supported language.</param>
/// <param name="TimeZone">The selected IANA time-zone identifier.</param>
/// <param name="Theme">The selected shell theme.</param>
/// <param name="Motion">The selected motion preference.</param>
/// <param name="Revision">The optimistic-concurrency revision.</param>
public sealed record ProfileResponse(
    Guid UserId,
    string UserName,
    string DisplayName,
    string PreferredLanguage,
    string TimeZone,
    string Theme,
    string Motion,
    int Revision);

/// <summary>Changes the authenticated user's shell preferences.</summary>
/// <param name="PreferredLanguage">Either <c>en</c> or <c>de</c>.</param>
/// <param name="TimeZone">A valid IANA time-zone identifier available to the server.</param>
/// <param name="Theme">Either <c>system</c>, <c>light</c> or <c>dark</c>.</param>
/// <param name="Motion">Either <c>enabled</c> or <c>reduced</c>.</param>
/// <param name="Revision">The revision read by the caller.</param>
public sealed record UpdateProfilePreferencesRequest(
    string PreferredLanguage,
    string TimeZone,
    string Theme,
    string Motion,
    int Revision);
