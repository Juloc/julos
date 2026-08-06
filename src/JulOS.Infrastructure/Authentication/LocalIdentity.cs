using Microsoft.AspNetCore.Identity;

namespace JulOS.Infrastructure.Authentication;

/// <summary>The local JulOS account persisted by ASP.NET Core Identity.</summary>
public sealed class LocalUser : IdentityUser<Guid>
{
    /// <summary>Gets or sets the localizable display name.</summary>
    public required string DisplayName { get; set; }

    /// <summary>Gets or sets the preferred BCP 47 language tag.</summary>
    public string PreferredLanguage { get; set; } = "en";

    /// <summary>Gets or sets the IANA time-zone identifier.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Gets or sets the requested shell theme.</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Gets or sets the requested shell motion mode.</summary>
    public string Motion { get; set; } = "enabled";

    /// <summary>Gets or sets when the account was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the account was last changed.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency revision.</summary>
    public int Revision { get; set; } = 1;
}

/// <summary>A JulOS role stored by ASP.NET Core Identity.</summary>
public sealed class LocalRole : IdentityRole<Guid>
{
    /// <summary>Gets or sets the operator-facing purpose of the role.</summary>
    public required string Description { get; set; }

    /// <summary>Gets or sets whether the role is part of the platform contract.</summary>
    public bool IsSystemRole { get; set; }

    /// <summary>Gets or sets the optimistic-concurrency revision.</summary>
    public int Revision { get; set; } = 1;
}

/// <summary>Names owned by the local identity boundary.</summary>
public static class LocalIdentityNames
{
    /// <summary>The immutable system role assigned to the initial administrator.</summary>
    public const string AdministratorRole = "Administrator";
}

internal sealed class AuthenticationSetupRow
{
    internal int Id { get; set; }

    internal Guid? AdministratorUserId { get; set; }

    internal DateTimeOffset? CompletedAtUtc { get; set; }
}
