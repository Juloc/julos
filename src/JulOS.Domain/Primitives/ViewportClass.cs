namespace JulOS.Domain.Primitives;

/// <summary>
/// The class of viewport a desktop is being shown in.
/// </summary>
/// <remarks>
/// Both the layout model and the application model need this vocabulary: a layout is
/// stored per viewport class, and an application declares which classes it supports.
/// It is a class rather than a pixel width because the stored layout of a phone must
/// never be overwritten by a resized desktop window that happens to be narrow.
/// </remarks>
public enum ViewportClass
{
    /// <summary>A pointer-driven viewport with room for overlapping windows.</summary>
    Desktop = 1,

    /// <summary>A touch-driven viewport that still shows more than one surface at a time.</summary>
    Tablet = 2,

    /// <summary>A narrow touch viewport that switches between tasks instead of overlapping them.</summary>
    Mobile = 3,
}
