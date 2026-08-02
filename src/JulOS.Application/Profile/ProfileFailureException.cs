using JulOS.Contracts.Profile;

namespace JulOS.Application.Profile;

/// <summary>Reasons profile management can refuse a request.</summary>
public enum ProfileFailureReason
{
    /// <summary>The submitted preferences are invalid.</summary>
    InvalidPreferences = 1,

    /// <summary>The requested local account does not exist.</summary>
    NotFound = 2,
}

/// <summary>A safe, typed refusal from profile management.</summary>
public sealed class ProfileFailureException : Exception
{
    /// <summary>Creates a profile refusal with one stable reason.</summary>
    public ProfileFailureException(ProfileFailureReason reason)
        : base(MessageFor(reason))
    {
        this.Reason = reason;
    }

    /// <summary>Creates a profile refusal with one stable reason and internal cause.</summary>
    public ProfileFailureException(ProfileFailureReason reason, Exception innerException)
        : base(MessageFor(reason), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>The stable refusal reason.</summary>
    public ProfileFailureReason Reason { get; }

    /// <summary>The public machine-readable code.</summary>
    public string Code => this.Reason switch
    {
        ProfileFailureReason.InvalidPreferences => ProfileErrorCodes.InvalidPreferences,
        ProfileFailureReason.NotFound => ProfileErrorCodes.NotFound,
        _ => throw new InvalidOperationException("Unknown profile failure."),
    };

    private static string MessageFor(ProfileFailureReason reason) => reason switch
    {
        ProfileFailureReason.InvalidPreferences => "The profile preferences are invalid.",
        ProfileFailureReason.NotFound => "The profile does not exist.",
        _ => "Profile management failed.",
    };
}
