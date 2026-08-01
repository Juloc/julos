namespace JulOS.Domain.Observability;

/// <summary>
/// How serious a detected condition is.
/// </summary>
/// <remarks>
/// Severity is a named, ordered value and never a colour. Colour alone is not a usable
/// severity signal for a colour-blind operator or in a monochrome export, so the name is
/// the signal and any colour a client adds is decoration on top of it.
/// </remarks>
public enum ProblemSeverity
{
    /// <summary>Worth knowing, but nothing is degraded.</summary>
    Information = 1,

    /// <summary>Something is degraded or will fail if it is left alone.</summary>
    Warning = 2,

    /// <summary>Something is not working now.</summary>
    Error = 3,

    /// <summary>Something is not working and the impact is broad or unrecoverable without action.</summary>
    Critical = 4,
}
