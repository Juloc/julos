using JulOS.Domain.Packages;
using JulOS.Domain.Primitives;

namespace JulOS.Domain.Applications;

/// <summary>
/// One concrete thing an application can be opened against.
/// </summary>
/// <remarks>
/// Identity is the owning package plus the stable external identity. A display name is
/// a label the source system supplies and changes freely, so it is never part of
/// identity: a renamed resource stays the same target and keeps its approval.
/// </remarks>
public sealed class LaunchTarget
{
    private LaunchTarget(
        LaunchTargetId id,
        ApplicationDefinitionId applicationId,
        PackageId owningPackageId,
        ExternalIdentity externalIdentity,
        string displayName,
        DateTimeOffset observedAtUtc)
    {
        this.Id = id;
        this.ApplicationDefinitionId = applicationId;
        this.OwningPackageId = owningPackageId;
        this.ExternalIdentity = externalIdentity;
        this.DisplayName = displayName;
        this.FirstObservedAtUtc = observedAtUtc;
        this.LastObservedAtUtc = observedAtUtc;
        this.ApprovalState = LaunchTargetApprovalState.Proposed;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this target record.</summary>
    public LaunchTargetId Id { get; }

    /// <summary>The application this target is opened with.</summary>
    public ApplicationDefinitionId ApplicationDefinitionId { get; }

    /// <summary>The package that observes and owns the target.</summary>
    public PackageId OwningPackageId { get; }

    /// <summary>The identity of the target in the system it comes from.</summary>
    public ExternalIdentity ExternalIdentity { get; }

    /// <summary>The label shown to the user. Never part of identity.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Whether a user has decided about this target.</summary>
    public LaunchTargetApprovalState ApprovalState { get; private set; }

    /// <summary>When the owning package first reported the target.</summary>
    public DateTimeOffset FirstObservedAtUtc { get; }

    /// <summary>When the owning package last reported the target.</summary>
    public DateTimeOffset LastObservedAtUtc { get; private set; }

    /// <summary>When a user approved the target, if one has.</summary>
    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Whether the target may currently be offered in the launcher.</summary>
    public bool IsOfferable => this.ApprovalState == LaunchTargetApprovalState.Approved;

    /// <summary>Records a target a package has proposed. It starts unapproved.</summary>
    public static LaunchTarget Propose(
        LaunchTargetId id,
        ApplicationDefinitionId applicationId,
        PackageId owningPackageId,
        ExternalIdentity externalIdentity,
        string displayName,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new LaunchTarget(
            id,
            applicationId,
            owningPackageId,
            externalIdentity,
            ValidatedDisplayName(displayName),
            timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Records that the owning package saw the target again, refreshing its label.
    /// </summary>
    /// <remarks>
    /// An observation never changes the approval state. That is what makes an ignored
    /// target stay ignored instead of reappearing as new on the next inventory pass.
    /// </remarks>
    public void Observe(string displayName, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.DisplayName = ValidatedDisplayName(displayName);
        this.LastObservedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    /// <summary>A user approves the target, so it appears in the launcher.</summary>
    public void Approve(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.ApprovalState = LaunchTargetApprovalState.Approved;
        this.ApprovedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    /// <summary>A user rejects the target. Later observations leave that decision in place.</summary>
    public void Ignore()
    {
        this.ApprovalState = LaunchTargetApprovalState.Ignored;
        this.ApprovedAtUtc = null;
        this.Revision = this.Revision.Next();
    }

    private static string ValidatedDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 256)
        {
            throw new DomainRuleViolationException(
                "launch_target.display_name.invalid",
                "A launch target label is non-empty and at most 256 characters.");
        }

        return displayName;
    }
}
