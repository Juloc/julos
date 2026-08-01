using JulOS.Domain.Primitives;

namespace JulOS.Domain.Layouts;

/// <summary>
/// The generated identity of one desktop window.
/// </summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct WindowId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
