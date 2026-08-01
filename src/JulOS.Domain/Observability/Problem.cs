using JulOS.Domain.Primitives;

namespace JulOS.Domain.Observability;

/// <summary>
/// One detected condition, however many times it has been observed.
/// </summary>
/// <remarks>
/// A problem is found again by its <see cref="ProblemIdentity"/> rather than created
/// again, so a condition observed on every poll stays one entry with a rising
/// observation count. A problem carries localization keys and no user-facing text, and
/// never carries a secret: it is shown to whoever can see the resource, which is not
/// necessarily whoever configured it.
/// </remarks>
public sealed class Problem
{
    private Problem(
        ProblemId id,
        ProblemIdentity identity,
        ProblemSeverity severity,
        string titleKey,
        DateTimeOffset observedAtUtc)
    {
        this.Id = id;
        this.Identity = identity;
        this.Severity = severity;
        this.TitleKey = titleKey;
        this.State = ProblemState.Active;
        this.FirstDetectedAtUtc = observedAtUtc;
        this.LastObservedAtUtc = observedAtUtc;
        this.ObservationCount = 1;
        this.Revision = Revision.Initial;
    }

    /// <summary>The generated identity of this record.</summary>
    public ProblemId Id { get; }

    /// <summary>What makes this problem the same problem across observations.</summary>
    public ProblemIdentity Identity { get; }

    /// <summary>How serious the condition currently is.</summary>
    public ProblemSeverity Severity { get; private set; }

    /// <summary>The localization key of the title. Never the title itself.</summary>
    public string TitleKey { get; private set; }

    /// <summary>Where the problem stands with the operator.</summary>
    public ProblemState State { get; private set; }

    /// <summary>When the condition was first detected.</summary>
    public DateTimeOffset FirstDetectedAtUtc { get; }

    /// <summary>When the condition was last observed.</summary>
    public DateTimeOffset LastObservedAtUtc { get; private set; }

    /// <summary>When an operator acknowledged the problem, if one has.</summary>
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }

    /// <summary>When the condition stopped being observed, if it has.</summary>
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>How many observations this record represents.</summary>
    public int ObservationCount { get; private set; }

    /// <summary>The concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Whether the problem should currently be shown to operators.</summary>
    public bool IsOpen => this.State is ProblemState.Active or ProblemState.Acknowledged;

    /// <summary>Records a condition observed for the first time.</summary>
    public static Problem Detect(
        ProblemId id,
        ProblemIdentity identity,
        ProblemSeverity severity,
        string titleKey,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);

        return new Problem(id, identity, severity, titleKey, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Records that the condition was observed again.
    /// </summary>
    /// <remarks>
    /// A resolved problem reopens, because the condition is back and hiding it would leave
    /// an operator believing a fixed system is still fixed. An acknowledged problem stays
    /// acknowledged and a suppressed one stays suppressed: both are decisions an operator
    /// made about this exact condition, and the next poll must not undo them.
    /// </remarks>
    /// <exception cref="DomainRuleViolationException">The observation belongs to a different problem.</exception>
    public void Observe(ProblemIdentity identity, ProblemSeverity severity, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (identity != this.Identity)
        {
            throw new DomainRuleViolationException(
                "problem.observation.identity_mismatch",
                "An observation of a different condition or resource is a different problem.");
        }

        if (this.State == ProblemState.Resolved)
        {
            this.State = ProblemState.Active;
            this.ResolvedAtUtc = null;
            this.AcknowledgedAtUtc = null;
        }

        this.Severity = severity;
        this.LastObservedAtUtc = timeProvider.GetUtcNow();
        this.ObservationCount = checked(this.ObservationCount + 1);
        this.Revision = this.Revision.Next();
    }

    /// <summary>An operator accepts that the problem is open and does not want it highlighted again.</summary>
    /// <exception cref="DomainRuleViolationException">The problem is not open.</exception>
    public void Acknowledge(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (this.State != ProblemState.Active)
        {
            throw new DomainRuleViolationException(
                "problem.transition.invalid",
                $"Only an active problem can be acknowledged, and this one is '{this.State}'.");
        }

        this.State = ProblemState.Acknowledged;
        this.AcknowledgedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    /// <summary>The condition is no longer observed.</summary>
    /// <exception cref="DomainRuleViolationException">The problem is already closed.</exception>
    public void Resolve(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (this.State == ProblemState.Resolved)
        {
            throw new DomainRuleViolationException(
                "problem.transition.invalid",
                "The problem is already resolved.");
        }

        this.State = ProblemState.Resolved;
        this.ResolvedAtUtc = timeProvider.GetUtcNow();
        this.Revision = this.Revision.Next();
    }

    /// <summary>An operator chooses not to be told about this condition.</summary>
    public void Suppress()
    {
        this.State = ProblemState.Suppressed;
        this.Revision = this.Revision.Next();
    }

    /// <summary>Points the problem at a different title key without changing its identity.</summary>
    public void RetitleTo(string titleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);

        this.TitleKey = titleKey;
        this.Revision = this.Revision.Next();
    }
}
