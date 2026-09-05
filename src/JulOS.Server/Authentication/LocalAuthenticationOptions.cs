namespace JulOS.Server.Authentication;

/// <summary>The validated local-authentication settings used by the Server.</summary>
internal sealed class LocalAuthenticationOptions
{
    internal const string SectionName = "Authentication";

    internal const int DefaultSessionTimeoutMinutes = 2880;
    internal const int MaximumSessionTimeoutMinutes = 10080;
    internal const int DefaultLockoutMinutes = 15;
    internal const int DefaultMaximumFailedAccessAttempts = 5;
    internal const int DefaultLoginPermitLimit = 5;
    internal const int DefaultLoginWindowSeconds = 60;

    internal int SessionTimeoutMinutes { get; init; } = DefaultSessionTimeoutMinutes;

    internal int LockoutMinutes { get; init; } = DefaultLockoutMinutes;

    internal int MaximumFailedAccessAttempts { get; init; } = DefaultMaximumFailedAccessAttempts;

    internal int LoginPermitLimit { get; init; } = DefaultLoginPermitLimit;

    internal int LoginWindowSeconds { get; init; } = DefaultLoginWindowSeconds;

    internal static LocalAuthenticationOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        var options = new LocalAuthenticationOptions
        {
            SessionTimeoutMinutes = section.GetValue(
                nameof(SessionTimeoutMinutes),
                DefaultSessionTimeoutMinutes),
            LockoutMinutes = section.GetValue(
                nameof(LockoutMinutes),
                DefaultLockoutMinutes),
            MaximumFailedAccessAttempts = section.GetValue(
                nameof(MaximumFailedAccessAttempts),
                DefaultMaximumFailedAccessAttempts),
            LoginPermitLimit = section.GetValue(
                nameof(LoginPermitLimit),
                DefaultLoginPermitLimit),
            LoginWindowSeconds = section.GetValue(
                nameof(LoginWindowSeconds),
                DefaultLoginWindowSeconds),
        };

        if (options.SessionTimeoutMinutes is < 1 or > MaximumSessionTimeoutMinutes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(SessionTimeoutMinutes)} must be between 1 and {MaximumSessionTimeoutMinutes}.");
        }

        if (options.LockoutMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LockoutMinutes)} must be between 1 and 1440.");
        }

        if (options.MaximumFailedAccessAttempts is < 3 or > 20)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaximumFailedAccessAttempts)} must be between 3 and 20.");
        }

        if (options.LoginPermitLimit is < 1 or > 100)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LoginPermitLimit)} must be between 1 and 100.");
        }

        if (options.LoginWindowSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LoginWindowSeconds)} must be between 1 and 3600.");
        }

        return options;
    }
}
