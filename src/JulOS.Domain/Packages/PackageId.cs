namespace JulOS.Domain.Packages;

/// <summary>
/// The stable published identity of a package, such as <c>de.juloc.julos.example</c>.
/// </summary>
/// <remarks>
/// This identity is chosen by the publisher and never changes, so it survives an update
/// and a reinstall. It is deliberately not the installation identifier: a package can be
/// removed and installed again, which produces a new installation record for the same
/// package. A display name is never an identity.
/// </remarks>
public readonly record struct PackageId
{
    private const int MaximumLength = 128;

    private PackageId(string value) => this.Value = value;

    /// <summary>The reverse domain name identifying the package.</summary>
    public string Value { get; }

    /// <summary>Reads a published package identity.</summary>
    /// <param name="value">A reverse domain name of lower-case segments separated by dots.</param>
    /// <exception cref="DomainRuleViolationException">The value is not a valid package identity.</exception>
    public static PackageId Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainRuleViolationException(
                "package.id.invalid",
                "A package identity is at least two dot-separated segments of lower-case letters, digits and hyphens.");
        }

        return new PackageId(value);
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
