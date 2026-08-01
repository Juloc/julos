using JulOS.Domain.Primitives;

namespace JulOS.Domain.Packages;

/// <summary>
/// One package's installation record and its lifecycle from installation through removal.
/// </summary>
/// <remarks>
/// The transition graph mirrors the package lifecycle: the linear
/// <see cref="PackageInstallationState.Installing"/> → <see cref="PackageInstallationState.Installed"/> →
/// <see cref="PackageInstallationState.Configuring"/> → <see cref="PackageInstallationState.Disabled"/> →
/// <see cref="PackageInstallationState.Starting"/> → <see cref="PackageInstallationState.Enabled"/> →
/// <see cref="PackageInstallationState.Stopping"/> → <see cref="PackageInstallationState.Disabled"/> path,
/// the rule that an installed package may enter <see cref="PackageInstallationState.Updating"/> or
/// <see cref="PackageInstallationState.Removing"/>, and the rule that an active transition or a running
/// worker may enter <see cref="PackageInstallationState.Faulted"/>. States with no active worker, namely
/// <see cref="PackageInstallationState.Installed"/> and <see cref="PackageInstallationState.Disabled"/>,
/// have nothing that can crash and therefore have no direct edge into
/// <see cref="PackageInstallationState.Faulted"/>. Entering <see cref="PackageInstallationState.Faulted"/>
/// always requires a reason, so <see cref="Fault"/> is the only way in; <see cref="TransitionTo"/> refuses
/// that target explicitly.
/// </remarks>
public sealed class PackageInstallation
{
    private static readonly Dictionary<PackageInstallationState, HashSet<PackageInstallationState>> AllowedTransitions = new()
    {
        [PackageInstallationState.Installing] = new() { PackageInstallationState.Installed, PackageInstallationState.Faulted },
        [PackageInstallationState.Installed] = new() { PackageInstallationState.Configuring, PackageInstallationState.Updating, PackageInstallationState.Removing },
        [PackageInstallationState.Configuring] = new() { PackageInstallationState.Disabled, PackageInstallationState.Faulted, PackageInstallationState.Updating, PackageInstallationState.Removing },
        [PackageInstallationState.Disabled] = new() { PackageInstallationState.Starting, PackageInstallationState.Updating, PackageInstallationState.Removing },
        [PackageInstallationState.Starting] = new() { PackageInstallationState.Enabled, PackageInstallationState.Faulted },
        [PackageInstallationState.Enabled] = new() { PackageInstallationState.Stopping, PackageInstallationState.Faulted, PackageInstallationState.Updating, PackageInstallationState.Removing },
        [PackageInstallationState.Stopping] = new() { PackageInstallationState.Disabled, PackageInstallationState.Faulted },
        [PackageInstallationState.Updating] = new() { PackageInstallationState.Installed, PackageInstallationState.Faulted },
        [PackageInstallationState.Faulted] = new() { PackageInstallationState.Updating, PackageInstallationState.Removing },
        [PackageInstallationState.Removing] = new() { PackageInstallationState.Faulted },
    };

    private PackageInstallation(
        PackageInstallationId id,
        PackageId packageId,
        PackageInstallationState state,
        Revision revision)
    {
        this.Id = id;
        this.PackageId = packageId;
        this.State = state;
        this.Revision = revision;
    }

    /// <summary>
    /// Begins a new installation. The record starts in <see cref="PackageInstallationState.Installing"/>
    /// at <see cref="Revision.Initial"/>.
    /// </summary>
    /// <param name="id">The generated identity of the new installation record.</param>
    /// <param name="packageId">The published identity of the package being installed.</param>
    public static PackageInstallation BeginInstallation(PackageInstallationId id, PackageId packageId) =>
        new(id, packageId, PackageInstallationState.Installing, Revision.Initial);

    /// <summary>The stable identity of this installation record.</summary>
    public PackageInstallationId Id { get; }

    /// <summary>
    /// The published identity of the installed package. Fixed for the life of the record:
    /// installing a different package produces a different record.
    /// </summary>
    public PackageId PackageId { get; }

    /// <summary>The current lifecycle state.</summary>
    public PackageInstallationState State { get; private set; }

    /// <summary>The concurrency revision. Every accepted transition moves it to the next value.</summary>
    public Revision Revision { get; private set; }

    /// <summary>
    /// The stable dotted code of the rule that caused the current fault, or <see langword="null"/> when
    /// the installation is not <see cref="PackageInstallationState.Faulted"/>.
    /// </summary>
    public string? FaultCode { get; private set; }

    /// <summary>
    /// A description of the current fault that contains no secret, or <see langword="null"/> when the
    /// installation is not <see cref="PackageInstallationState.Faulted"/>.
    /// </summary>
    public string? FaultDetail { get; private set; }

    /// <summary>
    /// The moment the current fault was recorded, or <see langword="null"/> when the installation is not
    /// <see cref="PackageInstallationState.Faulted"/>.
    /// </summary>
    public DateTimeOffset? FaultedAtUtc { get; private set; }

    /// <summary>
    /// Moves the installation to <paramref name="target"/> when the transition graph allows it from the
    /// current <see cref="State"/>. Any fault metadata recorded by a previous <see cref="Fault"/> call is
    /// cleared.
    /// </summary>
    /// <param name="target">
    /// The state to transition into. Must not be <see cref="PackageInstallationState.Faulted"/>; call
    /// <see cref="Fault"/> instead so the reason is recorded.
    /// </param>
    /// <exception cref="DomainRuleViolationException">
    /// <paramref name="target"/> is <see cref="PackageInstallationState.Faulted"/>, or the current state
    /// has no edge to <paramref name="target"/>.
    /// </exception>
    public void TransitionTo(PackageInstallationState target)
    {
        if (target == PackageInstallationState.Faulted)
        {
            throw new DomainRuleViolationException(
                "package.transition.fault_requires_reason",
                "A transition into the faulted state must go through Fault so the reason is recorded.");
        }

        this.EnsureEdgeExists(target);

        this.State = target;
        this.FaultCode = null;
        this.FaultDetail = null;
        this.FaultedAtUtc = null;
        this.Revision = this.Revision.Next();
    }

    /// <summary>
    /// Moves the installation to <see cref="PackageInstallationState.Faulted"/> when the transition graph
    /// allows it from the current <see cref="State"/>, recording why and when.
    /// </summary>
    /// <param name="faultCode">A stable dotted code identifying the cause, such as <c>package.worker.crashed</c>.</param>
    /// <param name="faultDetail">A description of the cause that contains no secret.</param>
    /// <param name="timeProvider">The clock used to record when the fault occurred.</param>
    /// <exception cref="DomainRuleViolationException">
    /// The current state has no edge to <see cref="PackageInstallationState.Faulted"/>.
    /// </exception>
    public void Fault(string faultCode, string faultDetail, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(faultCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultDetail);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.EnsureEdgeExists(PackageInstallationState.Faulted);

        this.State = PackageInstallationState.Faulted;
        this.FaultCode = faultCode;
        this.FaultDetail = faultDetail;
        this.FaultedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    private void EnsureEdgeExists(PackageInstallationState target)
    {
        if (!AllowedTransitions[this.State].Contains(target))
        {
            throw new DomainRuleViolationException(
                "package.transition.invalid",
                $"A package installation cannot move from '{this.State}' to '{target}'.");
        }
    }
}
