namespace JulOS.Domain.Applications;

/// <summary>
/// Whether a launch target may be offered to users.
/// </summary>
/// <remarks>
/// Observing a resource is not the same as managing it. A package can propose a target,
/// but only a user decides whether it appears in the launcher.
/// </remarks>
public enum LaunchTargetApprovalState
{
    /// <summary>A package proposed the target. It is not offered until a user approves it.</summary>
    Proposed = 1,

    /// <summary>A user approved the target and it is offered in the launcher.</summary>
    Approved = 2,

    /// <summary>A user rejected the target. Further observations must not offer it again.</summary>
    Ignored = 3,
}
