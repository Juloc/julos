namespace JulOS.Application.Profile;

/// <summary>The authenticated user's profile independent of persistence and HTTP types.</summary>
public sealed record UserProfile(
    Guid UserId,
    string UserName,
    string DisplayName,
    string PreferredLanguage,
    string TimeZone,
    string Theme,
    string Motion,
    int Revision);

/// <summary>Reads and changes one authenticated user's profile.</summary>
public interface IProfileService
{
    /// <summary>Reads the current profile for one user.</summary>
    Task<UserProfile> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Changes validated preferences when the caller's revision is current.</summary>
    Task<UserProfile> UpdatePreferencesAsync(
        Guid userId,
        string preferredLanguage,
        string timeZone,
        string theme,
        string motion,
        int revision,
        CancellationToken cancellationToken = default);
}
