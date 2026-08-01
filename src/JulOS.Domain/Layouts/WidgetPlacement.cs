namespace JulOS.Domain.Layouts;

/// <summary>
/// One widget placed on the desktop grid.
/// </summary>
/// <remarks>
/// Placement is expressed in grid units rather than pixels, so the same placement is
/// meaningful at any zoom level and on any display density.
/// </remarks>
public sealed class WidgetPlacement
{
    private const int LargestGridEdge = 64;

    private WidgetPlacement(
        WidgetPlacementId id,
        string widgetKey,
        int column,
        int row,
        int widthUnits,
        int heightUnits)
    {
        this.Id = id;
        this.WidgetKey = widgetKey;
        this.Column = column;
        this.Row = row;
        this.WidthUnits = widthUnits;
        this.HeightUnits = heightUnits;
    }

    /// <summary>The generated identity of this placement.</summary>
    public WidgetPlacementId Id { get; }

    /// <summary>The package-declared key of the widget being shown.</summary>
    public string WidgetKey { get; }

    /// <summary>The zero-based grid column of the top-left cell.</summary>
    public int Column { get; private set; }

    /// <summary>The zero-based grid row of the top-left cell.</summary>
    public int Row { get; private set; }

    /// <summary>The width in grid units.</summary>
    public int WidthUnits { get; private set; }

    /// <summary>The height in grid units.</summary>
    public int HeightUnits { get; private set; }

    /// <summary>Places a widget on the grid.</summary>
    /// <exception cref="DomainRuleViolationException">The placement is outside the grid or has no area.</exception>
    public static WidgetPlacement Place(
        WidgetPlacementId id,
        string widgetKey,
        int column,
        int row,
        int widthUnits,
        int heightUnits)
    {
        if (string.IsNullOrWhiteSpace(widgetKey))
        {
            throw new DomainRuleViolationException(
                "layout.widget.key_missing",
                "A widget placement must name the widget it shows.");
        }

        EnsureOnGrid(column, row, widthUnits, heightUnits);

        return new WidgetPlacement(id, widgetKey, column, row, widthUnits, heightUnits);
    }

    /// <summary>Moves or resizes the widget on the grid.</summary>
    /// <exception cref="DomainRuleViolationException">The placement is outside the grid or has no area.</exception>
    public void MoveTo(int column, int row, int widthUnits, int heightUnits)
    {
        EnsureOnGrid(column, row, widthUnits, heightUnits);

        this.Column = column;
        this.Row = row;
        this.WidthUnits = widthUnits;
        this.HeightUnits = heightUnits;
    }

    /// <summary>Returns whether this placement overlaps another one.</summary>
    public bool Overlaps(WidgetPlacement other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return this.Column < other.Column + other.WidthUnits
            && other.Column < this.Column + this.WidthUnits
            && this.Row < other.Row + other.HeightUnits
            && other.Row < this.Row + this.HeightUnits;
    }

    private static void EnsureOnGrid(int column, int row, int widthUnits, int heightUnits)
    {
        if (widthUnits <= 0 || heightUnits <= 0)
        {
            throw new DomainRuleViolationException(
                "layout.widget.size_not_positive",
                "A widget with no area would occupy the grid without ever being visible.");
        }

        if (column < 0 || row < 0)
        {
            throw new DomainRuleViolationException(
                "layout.widget.outside_grid",
                "A widget cannot start before the first grid cell.");
        }

        if (column + widthUnits > LargestGridEdge || row + heightUnits > LargestGridEdge)
        {
            throw new DomainRuleViolationException(
                "layout.widget.outside_grid",
                $"A widget cannot extend past grid unit {LargestGridEdge}.");
        }
    }
}
