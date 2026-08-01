namespace JulOS.Domain.Observability;

/// <summary>
/// How a recorded operation ended.
/// </summary>
/// <remarks>
/// A refusal is recorded separately from a failure. Someone repeatedly being denied an
/// operation is a security signal; something breaking is an operational one, and merging
/// them would hide the first inside the noise of the second.
/// </remarks>
public enum AuditOutcome
{
    /// <summary>The operation reached the state it requested.</summary>
    Succeeded = 1,

    /// <summary>The operation was attempted and did not reach that state.</summary>
    Failed = 2,

    /// <summary>The caller was not permitted to attempt the operation.</summary>
    Denied = 3,
}
