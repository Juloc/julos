namespace JulOS.Domain.Permissions;

/// <summary>
/// The identity of one resource a <see cref="PermissionScope"/> can narrow access to, such
/// as one Agent, connection or external resource.
/// </summary>
/// <remarks>
/// The value is opaque to the permission model: it is minted and interpreted by whichever
/// part of Core owns the resource. Scope evaluation only ever compares it for equality, so
/// this type stays generic instead of naming a resource kind.
/// </remarks>
public readonly record struct PermissionResourceId
{
    private const int MaximumLength = 256;

    private PermissionResourceId(string value) => this.Value = value;

    /// <summary>The resource identity value, opaque to the permission model.</summary>
    public string Value { get; }

    /// <summary>Reads a stable resource identity a permission scope refers to.</summary>
    /// <param name="value">A non-empty value without leading or trailing whitespace or a control character.</param>
    /// <exception cref="DomainRuleViolationException">The value cannot serve as a stable resource identity.</exception>
    public static PermissionResourceId Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumLength
            || value.Length != value.Trim().Length
            || value.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "permission.resource_id.invalid",
                "A permission resource identity is non-empty, has no surrounding whitespace and contains no control character.");
        }

        return new PermissionResourceId(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
