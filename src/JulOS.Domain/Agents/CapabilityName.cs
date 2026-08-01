namespace JulOS.Domain.Agents;

/// <summary>
/// The name of one capability family an Agent can advertise, such as <c>system.metrics</c>.
/// </summary>
/// <remarks>
/// The Agent hosts a small, closed set of capability families, but Domain does not enumerate
/// them: naming a product or a fixed capability list here would make Domain depend on
/// decisions that belong to the Agent binary and the packages that consume the capability.
/// Instead this type validates the generic shape every capability name must have, exactly
/// like <see cref="JulOS.Domain.Packages.PackageId"/> validates the shape of a package
/// identity without knowing which packages exist.
/// </remarks>
public readonly record struct CapabilityName
{
    private const int MaximumLength = 128;

    private CapabilityName(string value) => this.Value = value;

    /// <summary>The dotted capability name.</summary>
    public string Value { get; }

    /// <summary>Reads a capability name advertised by an Agent.</summary>
    /// <param name="value">A reverse-hierarchy name of lower-case dot-separated segments, such as <c>system.metrics</c>.</param>
    /// <exception cref="DomainRuleViolationException">The value is not a valid capability name.</exception>
    public static CapabilityName Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainRuleViolationException(
                "agent_capability.name.invalid",
                "A capability name is at least two dot-separated segments of lower-case letters, digits and hyphens.");
        }

        return new CapabilityName(value);
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
