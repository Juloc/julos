namespace JulOS.Domain.Primitives;

/// <summary>
/// The concurrency revision of one mutable core record.
/// </summary>
/// <remarks>
/// Every update carries the revision it was based on. A stored record whose revision
/// has moved on rejects the update, so a slower writer cannot silently overwrite a
/// newer one. <see cref="Initial"/> is the revision of a freshly created record.
/// </remarks>
public readonly record struct Revision : IComparable<Revision>
{
    /// <summary>The revision every newly created record starts at.</summary>
    public static Revision Initial => new(1);

    private Revision(int value) => this.Value = value;

    /// <summary>The numeric value. Always one or greater.</summary>
    public int Value { get; }

    /// <summary>Reads a revision that was stored or received from a client.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value does not identify a stored revision.</exception>
    public static Revision From(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, Initial.Value);

        return new Revision(value);
    }

    /// <summary>Returns the revision this record has after one accepted update.</summary>
    public Revision Next() => new(checked(this.Value + 1));

    /// <summary>Compares two revisions by age.</summary>
    public int CompareTo(Revision other) => this.Value.CompareTo(other.Value);

    /// <summary>Returns whether the left revision is older than the right one.</summary>
    public static bool operator <(Revision left, Revision right) => left.CompareTo(right) < 0;

    /// <summary>Returns whether the left revision is older than or equal to the right one.</summary>
    public static bool operator <=(Revision left, Revision right) => left.CompareTo(right) <= 0;

    /// <summary>Returns whether the left revision is newer than the right one.</summary>
    public static bool operator >(Revision left, Revision right) => left.CompareTo(right) > 0;

    /// <summary>Returns whether the left revision is newer than or equal to the right one.</summary>
    public static bool operator >=(Revision left, Revision right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
