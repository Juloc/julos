namespace JulOS.Domain.Permissions;

/// <summary>
/// One granular permission string such as <c>packages.read</c> or <c>packages.install</c>.
/// </summary>
/// <remarks>
/// A permission is a validated value rather than a bare string, so a typo produces a
/// rejected assignment instead of a permission that silently never matches anything.
/// Read and control permissions are unrelated values by construction: two permission
/// names are equal only when their text is identical, so a granted <c>*.read</c>
/// permission never equals, and therefore never satisfies, the matching <c>*.control</c>
/// permission. Nothing in this type or in <see cref="PermissionEvaluator"/> derives one
/// permission from another.
/// </remarks>
public readonly record struct PermissionName
{
    private const int MaximumLength = 128;

    private PermissionName(string value) => this.Value = value;

    /// <summary>The dotted permission value.</summary>
    public string Value { get; }

    /// <summary>Reads a permission string declared by Core or a package.</summary>
    /// <param name="value">At least two dot-separated segments of lower-case letters, digits and hyphens.</param>
    /// <exception cref="DomainRuleViolationException">The value is not a valid permission string.</exception>
    public static PermissionName Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainRuleViolationException(
                "permission.name.invalid",
                "A permission is at least two dot-separated segments of lower-case letters, digits and hyphens.");
        }

        return new PermissionName(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;

    private static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        var segments = value.Split('.');

        return segments.Length >= 2 && Array.TrueForAll(segments, IsValidSegment);
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment[0] == '-' || segment[^1] == '-')
        {
            return false;
        }

        foreach (var character in segment)
        {
            var allowed = char.IsAsciiDigit(character) || char.IsAsciiLetterLower(character) || character == '-';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
