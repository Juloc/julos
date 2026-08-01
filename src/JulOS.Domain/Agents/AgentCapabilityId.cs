using JulOS.Domain.Primitives;

namespace JulOS.Domain.Agents;

/// <summary>
/// The generated identity of one advertised Agent capability record.
/// </summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct AgentCapabilityId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
