namespace JulOS.Domain.Applications;

/// <summary>
/// The identity a launch target has in the system it comes from.
/// </summary>
/// <remarks>
/// This value must survive the external resource being recreated, so it is never an
/// ephemeral runtime identifier, an address or a display name. Recreating a resource
/// under the same external identity updates one target; a new identity creates a new
/// one, which is what keeps approvals and stored windows attached to the right thing.
/// </remarks>
public readonly record struct ExternalIdentity
{
    private const int MaximumLength = 256;

    private ExternalIdentity(string value) => this.Value = value;

    /// <summary>The identity value, opaque to Core.</summary>
    public string Value { get; }

    /// <summary>Reads a stable external identity supplied by the owning package.</summary>
    /// <param name="value">A non-empty value without leading or trailing whitespace.</param>
    /// <exception cref="DomainRuleViolationException">The value cannot serve as a stable identity.</exception>
    public static ExternalIdentity Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumLength
            || value.Length != value.Trim().Length
            || value.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "launch_target.external_identity.invalid",
                "A stable external identity is non-empty, has no surrounding whitespace and contains no control character.");
        }

        return new ExternalIdentity(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
