using JulOS.Domain.Primitives;

namespace JulOS.Domain.Applications;

/// <summary>
/// The generated identity of one launch target.
/// </summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct LaunchTargetId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
