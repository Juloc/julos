namespace JulOS.Contracts.Profile;

/// <summary>Stable public failures owned by profile management.</summary>
public static class ProfileErrorCodes
{
    /// <summary>The submitted preference representation is invalid.</summary>
    public const string InvalidPreferences = "profile.preferences_invalid";

    /// <summary>The authenticated account no longer exists.</summary>
    public const string NotFound = "profile.not_found";
}
