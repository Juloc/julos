using JulOS.Domain.Primitives;

namespace JulOS.Domain.Packages;

/// <summary>
/// The stable identity of one package installation record.
/// </summary>
/// <param name="Value">The generated identifier value.</param>
public readonly record struct PackageInstallationId(Guid Value)
{
    /// <summary>The generated identifier value, validated to identify an entity.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
