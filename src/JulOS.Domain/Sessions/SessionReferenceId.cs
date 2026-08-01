using JulOS.Domain.Primitives;

namespace JulOS.Domain.Sessions;

/// <summary>
/// The stable identity of one session reference.
/// </summary>
/// <remarks>
/// Generated once by Server when a session reference is created, so it survives the
/// runtime, connection or transport underneath it being recreated.
/// </remarks>
public readonly record struct SessionReferenceId(Guid Value)
{
    /// <summary>The wrapped identifier value.</summary>
    public Guid Value { get; } = EntityIdentifier.Validated(Value);
}
