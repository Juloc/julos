using JulOS.Domain.Primitives;

namespace JulOS.Infrastructure.Identifiers;

/// <summary>
/// Creates time-ordered identifiers.
/// </summary>
/// <remarks>
/// Version 7 identifiers embed their creation time in the high bits, so rows inserted
/// over time stay close together in a B-tree index instead of scattering the way a
/// random version 4 identifier does. The timestamp comes from the injected
/// <see cref="TimeProvider"/>, which keeps generated identities deterministic in tests.
/// </remarks>
public sealed class TimeOrderedIdentifierGenerator : IIdentifierGenerator
{
    private readonly TimeProvider timeProvider;

    /// <summary>Creates the generator for one time source.</summary>
    /// <param name="timeProvider">The time source; use <see cref="TimeProvider.System"/> in production.</param>
    public TimeOrderedIdentifierGenerator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Guid Create() => Guid.CreateVersion7(this.timeProvider.GetUtcNow());
}
