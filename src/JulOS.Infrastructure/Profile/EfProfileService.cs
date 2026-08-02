using JulOS.Application.Concurrency;
using JulOS.Application.Profile;
using JulOS.Contracts.Profile;
using JulOS.Infrastructure.Authentication;
using JulOS.Infrastructure.Persistence.Core;

using Microsoft.EntityFrameworkCore;

namespace JulOS.Infrastructure.Profile;

/// <summary>Reads and changes local profiles in the authoritative Core store.</summary>
public sealed class EfProfileService : IProfileService
{
    private const int MaximumTimeZoneLength = 128;
    private readonly CoreDbContext context;

    /// <summary>Creates the Core-backed profile service.</summary>
    /// <param name="context">The authoritative Core database context.</param>
    public EfProfileService(CoreDbContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<UserProfile> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var user = await this.context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProfileFailureException(ProfileFailureReason.NotFound);

        return ToProfile(user);
    }

    /// <inheritdoc />
    public async Task<UserProfile> UpdatePreferencesAsync(
        Guid userId,
        string preferredLanguage,
        string timeZone,
        string theme,
        string motion,
        int revision,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        Validate(preferredLanguage, timeZone, theme, motion, revision);

        var user = await this.context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new ProfileFailureException(ProfileFailureReason.NotFound);

        if (user.Revision != revision)
        {
            throw new ConcurrencyConflictException(
                user.Revision,
                new InvalidOperationException("The profile changed concurrently."));
        }

        user.PreferredLanguage = preferredLanguage;
        user.TimeZone = timeZone;
        user.Theme = theme;
        user.Motion = motion;

        await this.context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToProfile(user);
    }

    private static void EnsureUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ProfileFailureException(ProfileFailureReason.NotFound);
        }
    }

    private static void Validate(
        string preferredLanguage,
        string timeZone,
        string theme,
        string motion,
        int revision)
    {
        if ((preferredLanguage != ProfileLanguages.English
                && preferredLanguage != ProfileLanguages.German)
            || string.IsNullOrWhiteSpace(timeZone)
            || timeZone.Length > MaximumTimeZoneLength
            || (theme != ProfileThemes.System
                && theme != ProfileThemes.Light
                && theme != ProfileThemes.Dark)
            || (motion != ProfileMotionPreferences.Enabled
                && motion != ProfileMotionPreferences.Reduced)
            || revision < 1
            || !IsValidTimeZone(timeZone))
        {
            throw new ProfileFailureException(ProfileFailureReason.InvalidPreferences);
        }
    }

    private static bool IsValidTimeZone(string timeZone)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static UserProfile ToProfile(LocalUser user) => new(
        user.Id,
        user.UserName
            ?? throw new InvalidOperationException("A persisted local user has no username."),
        user.DisplayName,
        user.PreferredLanguage,
        user.TimeZone,
        user.Theme,
        user.Motion,
        user.Revision);
}
