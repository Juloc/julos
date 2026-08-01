namespace JulOS.Domain.Agents;

/// <summary>
/// The metadata payload an Agent reports alongside one advertised capability.
/// </summary>
/// <remarks>
/// The payload is opaque to Core: its shape is defined by <see cref="CapabilityVersion"/>
/// and interpreted by whichever package or Application service requests the capability, not
/// by this type. It must never carry a credential, key or token value. Domain has no
/// concept of a secret payload to validate against, so keeping any such value out of this
/// field is an obligation of the Agent and of the service that stores it; see the Secret
/// Reference glossary entry for where credential material actually belongs.
/// </remarks>
public readonly record struct CapabilityMetadata
{
    private const int MaximumLength = 8192;

    private CapabilityMetadata(string value) => this.Value = value;

    /// <summary>A capability with no metadata payload.</summary>
    public static CapabilityMetadata Empty => new(string.Empty);

    /// <summary>The opaque payload value.</summary>
    public string Value { get; }

    /// <summary>Reads a metadata payload reported by an Agent.</summary>
    /// <param name="value">The payload. May be empty.</param>
    /// <exception cref="DomainRuleViolationException">The value is longer than the platform allows.</exception>
    public static CapabilityMetadata Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > MaximumLength)
        {
            throw new DomainRuleViolationException(
                "agent_capability.metadata.too_long",
                $"A capability metadata payload is at most {MaximumLength} characters.");
        }

        return new CapabilityMetadata(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
