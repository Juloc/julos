using System.Runtime.CompilerServices;

namespace JulOS.Domain.Primitives;

/// <summary>
/// Guards for server-generated entity identifiers.
/// </summary>
/// <remarks>
/// Every core entity declares its own identifier type, for example
/// <c>public readonly record struct AgentId(Guid Value)</c>, so one entity's identifier
/// cannot be passed where another is expected. Each of those types validates its value
/// through <see cref="Validated"/>.
/// </remarks>
public static class EntityIdentifier
{
    /// <summary>
    /// Returns the value when it identifies an entity, and throws when it does not.
    /// </summary>
    /// <param name="value">The identifier value produced by <see cref="IIdentifierGenerator"/>.</param>
    /// <param name="name">The caller's expression, supplied by the compiler.</param>
    /// <exception cref="ArgumentException">The value is empty and identifies nothing.</exception>
    public static Guid Validated(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An entity identifier cannot be empty.", name);
        }

        return value;
    }
}
