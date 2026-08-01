using JulOS.Domain.Primitives;

namespace JulOS.Domain.Sessions;

/// <summary>
/// The lifecycle of one protocol-neutral session reference.
/// </summary>
/// <remarks>
/// A session reference tracks runtime state independently of any presentation window.
/// Closing a window never calls <see cref="Disconnect"/>, <see cref="Suspend"/> or
/// <see cref="Terminate"/> directly; it goes through <see cref="ApplyWindowClosed"/>, which
/// applies the configured <see cref="LifecyclePolicy"/> instead of always ending the
/// session. Window close and session termination are therefore distinct operations that
/// happen to coincide only under <see cref="SessionLifecyclePolicy.TerminateOnWindowClose"/>.
/// </remarks>
public sealed class SessionReference
{
    private readonly TimeProvider timeProvider;

    private SessionReference(
        SessionReferenceId id,
        SessionRequest request,
        SessionLifecyclePolicy lifecyclePolicy,
        TimeProvider timeProvider,
        DateTimeOffset createdAtUtc)
    {
        this.Id = id;
        this.Request = request;
        this.LifecyclePolicy = lifecyclePolicy;
        this.timeProvider = timeProvider;
        this.CreatedAtUtc = createdAtUtc;
        this.State = SessionState.Requested;
        this.Revision = Revision.Initial;
    }

    /// <summary>The stable identity of this session reference.</summary>
    public SessionReferenceId Id { get; }

    /// <summary>The request this session reference was created from.</summary>
    public SessionRequest Request { get; }

    /// <summary>The effect that closing the owning window has on this session reference.</summary>
    public SessionLifecyclePolicy LifecyclePolicy { get; }

    /// <summary>The current protocol-neutral state.</summary>
    public SessionState State { get; private set; }

    /// <summary>The code recorded for the most recent abnormal disconnect or end, if any.</summary>
    public SessionFailureCode? FailureCode { get; private set; }

    /// <summary>The moment this session reference was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The moment a runtime interaction most recently connected, if it ever has.</summary>
    public DateTimeOffset? ConnectedAtUtc { get; private set; }

    /// <summary>The moment this session reference reached its final state, if it has.</summary>
    public DateTimeOffset? EndedAtUtc { get; private set; }

    /// <summary>The optimistic concurrency revision.</summary>
    public Revision Revision { get; private set; }

    /// <summary>Creates a new session reference for one request. It starts in <see cref="SessionState.Requested"/>.</summary>
    /// <param name="id">The stable identity to create the reference with.</param>
    /// <param name="request">The request the session reference is created from.</param>
    /// <param name="lifecyclePolicy">The effect closing the owning window has on the reference.</param>
    /// <param name="timeProvider">The clock the reference records its timestamps from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> or <paramref name="timeProvider"/> is null.</exception>
    public static SessionReference Create(
        SessionReferenceId id,
        SessionRequest request,
        SessionLifecyclePolicy lifecyclePolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new SessionReference(id, request, lifecyclePolicy, timeProvider, timeProvider.GetUtcNow());
    }

    /// <summary>Connects a runtime interaction, including a reconnect after a disconnect or a resume after a suspend.</summary>
    /// <exception cref="DomainRuleViolationException">The current state cannot connect.</exception>
    public void Connect()
    {
        this.EnsureTransitionAllowed(
            this.State is SessionState.Requested or SessionState.Disconnected or SessionState.Suspended,
            "connect");

        this.ConnectedAtUtc = this.timeProvider.GetUtcNow();
        this.FailureCode = null;
        this.Transition(SessionState.Connected);
    }

    /// <summary>Disconnects the runtime interaction while keeping the session reference alive to reconnect.</summary>
    /// <param name="failureCode">The code explaining an abnormal disconnect, or null for a clean one.</param>
    /// <exception cref="DomainRuleViolationException">The current state is not connected.</exception>
    public void Disconnect(SessionFailureCode? failureCode = null)
    {
        this.EnsureTransitionAllowed(this.State == SessionState.Connected, "disconnect");

        this.FailureCode = failureCode;
        this.Transition(SessionState.Disconnected);
    }

    /// <summary>Suspends the session reference until it is resumed with <see cref="Connect"/>.</summary>
    /// <exception cref="DomainRuleViolationException">The current state is not connected.</exception>
    public void Suspend()
    {
        this.EnsureTransitionAllowed(this.State == SessionState.Connected, "suspend");

        this.Transition(SessionState.Suspended);
    }

    /// <summary>Ends the session reference. An ended session reference never resumes.</summary>
    /// <param name="failureCode">The code explaining an abnormal end, or null for a requested end.</param>
    /// <exception cref="DomainRuleViolationException">The session reference already ended.</exception>
    public void Terminate(SessionFailureCode? failureCode = null)
    {
        this.EnsureTransitionAllowed(this.State != SessionState.Ended, "terminate");

        this.FailureCode = failureCode;
        this.EndedAtUtc = this.timeProvider.GetUtcNow();
        this.Transition(SessionState.Ended);
    }

    /// <summary>
    /// Applies the effect of the owning window closing, as configured by <see cref="LifecyclePolicy"/>.
    /// </summary>
    /// <remarks>
    /// This is the only place a window closing reaches a session reference, and it never
    /// assumes termination: <see cref="SessionLifecyclePolicy.DisconnectOnWindowClose"/> and
    /// <see cref="SessionLifecyclePolicy.SuspendOnWindowClose"/> keep the session reference
    /// alive for a later reconnect, exactly like an explicit <see cref="Disconnect"/> or
    /// <see cref="Suspend"/> call would.
    /// </remarks>
    /// <exception cref="DomainRuleViolationException">The current state cannot apply the configured effect.</exception>
    public void ApplyWindowClosed()
    {
        switch (this.LifecyclePolicy)
        {
            case SessionLifecyclePolicy.DisconnectOnWindowClose:
                this.Disconnect();
                break;
            case SessionLifecyclePolicy.SuspendOnWindowClose:
                this.Suspend();
                break;
            case SessionLifecyclePolicy.TerminateOnWindowClose:
                this.Terminate();
                break;
            default:
                throw new DomainRuleViolationException(
                    "session.lifecycle_policy.unknown",
                    $"Lifecycle policy '{this.LifecyclePolicy}' has no defined window-close effect.");
        }
    }

    private void EnsureTransitionAllowed(bool isAllowed, string operation)
    {
        if (!isAllowed)
        {
            throw new DomainRuleViolationException(
                "session.transition.invalid",
                $"Cannot {operation} session reference '{this.Id.Value}' while it is {this.State}.");
        }
    }

    private void Transition(SessionState state)
    {
        this.State = state;
        this.Revision = this.Revision.Next();
    }
}
