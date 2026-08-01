using JulOS.Domain.Primitives;

namespace JulOS.Domain.Permissions;

/// <summary>
/// The generated identity of the user or role a <see cref="PermissionSubject"/> refers to.
/// </summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct PermissionSubjectId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
