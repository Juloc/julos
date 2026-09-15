namespace JulOS.Domain.Layouts;

/// <summary>
/// How a stored layout arranges its windows.
/// </summary>
/// <remarks>
/// The mode is part of the layout, not of the client: reopening the same workspace on a
/// second device must present the windows the same way. The phone modes exist because a
/// phone shows at most two foreground windows, which is a different arrangement rule
/// rather than a smaller version of the same one.
/// </remarks>
public enum PresentationMode
{
    /// <summary>Overlapping windows the user positions freely.</summary>
    Freeform = 1,

    /// <summary>Windows arranged by the Shell without overlap.</summary>
    Tiled = 2,

    /// <summary>A phone workspace with no foreground window.</summary>
    PhoneEmpty = 3,

    /// <summary>A phone workspace showing exactly one foreground window.</summary>
    PhoneSingle = 4,

    /// <summary>A phone workspace showing two foreground windows above each other.</summary>
    PhoneSplit = 5,
}
