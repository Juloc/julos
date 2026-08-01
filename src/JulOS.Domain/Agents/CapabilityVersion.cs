using System.Globalization;

namespace JulOS.Domain.Agents;

/// <summary>
/// A version number carried by an advertised capability, such as the capability contract
/// version or the schema version of its metadata payload.
/// </summary>
/// <remarks>
/// Unlike <see cref="JulOS.Domain.Primitives.Revision"/>, which is Core's own optimistic
/// concurrency counter, this value is reported by the Agent and describes a contract the
/// Agent implements. Reusing <c>Revision</c> for it would mix a Core-owned mechanism with
/// data that arrives from outside Core, so this is a small, separate type with the same
/// "positive and monotonic" shape.
/// </remarks>
public readonly record struct CapabilityVersion : IComparable<CapabilityVersion>
{
    /// <summary>The version every capability starts at.</summary>
    public static CapabilityVersion Initial => new(1);

    private CapabilityVersion(int value) => this.Value = value;

    /// <summary>The numeric value. Always one or greater.</summary>
    public int Value { get; }

    /// <summary>Reads a version number reported by an Agent.</summary>
    /// <param name="value">A value of one or greater.</param>
    /// <exception cref="DomainRuleViolationException"><paramref name="value"/> is less than one.</exception>
    public static CapabilityVersion From(int value)
    {
        if (value < Initial.Value)
        {
            throw new DomainRuleViolationException(
                "agent_capability.version.invalid",
                "A capability version number is one or greater.");
        }

        return new CapabilityVersion(value);
    }

    /// <summary>Compares two version numbers by age.</summary>
    public int CompareTo(CapabilityVersion other) => this.Value.CompareTo(other.Value);

    /// <summary>Returns whether the left version is older than the right one.</summary>
    public static bool operator <(CapabilityVersion left, CapabilityVersion right) => left.CompareTo(right) < 0;

    /// <summary>Returns whether the left version is older than or equal to the right one.</summary>
    public static bool operator <=(CapabilityVersion left, CapabilityVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Returns whether the left version is newer than the right one.</summary>
    public static bool operator >(CapabilityVersion left, CapabilityVersion right) => left.CompareTo(right) > 0;

    /// <summary>Returns whether the left version is newer than or equal to the right one.</summary>
    public static bool operator >=(CapabilityVersion left, CapabilityVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString(CultureInfo.InvariantCulture);
}
