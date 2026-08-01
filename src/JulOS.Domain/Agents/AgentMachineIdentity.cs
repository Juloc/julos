namespace JulOS.Domain.Agents;

/// <summary>
/// The stable identity of the host an Agent runs on.
/// </summary>
/// <remarks>
/// This value must survive a reinstall of the Agent binary on the same host, so it is
/// never an address, a hostname alone or an ephemeral runtime identifier: none of those
/// reliably identify the same physical or virtual machine across reinstalls, and an
/// address can move to a different host entirely. The generated <see cref="AgentId"/> is
/// a different concept: it identifies one enrollment record, so revoking and re-enrolling
/// the same host produces a new <see cref="AgentId"/> that carries the same machine
/// identity.
/// </remarks>
public readonly record struct AgentMachineIdentity
{
    private const int MaximumLength = 256;

    private AgentMachineIdentity(string value) => this.Value = value;

    /// <summary>The identity value, opaque to Core.</summary>
    public string Value { get; }

    /// <summary>Reads a stable machine identity reported by the Agent.</summary>
    /// <param name="value">A non-empty value without leading or trailing whitespace.</param>
    /// <exception cref="DomainRuleViolationException">The value cannot serve as a stable identity.</exception>
    public static AgentMachineIdentity Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumLength
            || value.Length != value.Trim().Length
            || value.Any(char.IsControl))
        {
            throw new DomainRuleViolationException(
                "agent.machine_identity.invalid",
                "A machine identity is non-empty, has no surrounding whitespace and contains no control character.");
        }

        return new AgentMachineIdentity(value);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
