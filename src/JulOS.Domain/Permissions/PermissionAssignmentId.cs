using JulOS.Domain.Primitives;

namespace JulOS.Domain.Permissions;

/// <summary>
/// The generated identity of one permission assignment record.
/// </summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct PermissionAssignmentId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
