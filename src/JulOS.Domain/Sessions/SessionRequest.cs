namespace JulOS.Domain.Sessions;

/// <summary>
/// The protocol-neutral request that a session reference is created from.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> and <see cref="TargetReference"/> are opaque to Core: they are
/// declared and interpreted by the owning package, never by Core itself.
/// </remarks>
public sealed record SessionRequest
{
    /// <summary>Creates the request a new session reference is created from.</summary>
    /// <param name="kind">The package-declared, protocol-neutral kind of session being requested.</param>
    /// <param name="targetReference">The stable identity of the destination the session connects to.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> or <paramref name="targetReference"/> is empty.</exception>
    public SessionRequest(string kind, string targetReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetReference);

        this.Kind = kind;
        this.TargetReference = targetReference;
    }

    /// <summary>The package-declared, protocol-neutral kind of session being requested.</summary>
    public string Kind { get; }

    /// <summary>The stable identity of the destination the session connects to.</summary>
    public string TargetReference { get; }
}
