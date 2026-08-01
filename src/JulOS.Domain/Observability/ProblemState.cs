namespace JulOS.Domain.Observability;

/// <summary>
/// Where a problem stands with the operator.
/// </summary>
public enum ProblemState
{
    /// <summary>Detected and not yet dealt with.</summary>
    Active = 1,

    /// <summary>An operator has seen it and accepted that it is still open.</summary>
    Acknowledged = 2,

    /// <summary>The condition is no longer observed.</summary>
    Resolved = 3,

    /// <summary>An operator has chosen not to be told about this condition for now.</summary>
    Suppressed = 4,
}
