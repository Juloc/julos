using JulOS.Domain.Primitives;
using JulOS.Domain.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Sessions;

/// <summary>Verifies the protocol-neutral session-reference lifecycle.</summary>
[TestClass]
public sealed class SessionReferenceTests
{
    private static SessionRequest ARequest() => new("de.juloc.julos.browser.session", "target-1");

    private static SessionReference ACreatedReference(
        FakeTimeProvider timeProvider,
        SessionLifecyclePolicy lifecyclePolicy = SessionLifecyclePolicy.DisconnectOnWindowClose) =>
        SessionReference.Create(new SessionReferenceId(Guid.CreateVersion7()), ARequest(), lifecyclePolicy, timeProvider);

    [TestMethod]
    public void ACreatedSessionReferenceStartsRequestedAtTheInitialRevision()
    {
        var timeProvider = new FakeTimeProvider();

        var session = ACreatedReference(timeProvider);

        Assert.AreEqual(SessionState.Requested, session.State);
        Assert.AreEqual(Revision.Initial, session.Revision);
        Assert.AreEqual(timeProvider.GetUtcNow(), session.CreatedAtUtc);
        Assert.IsNull(session.ConnectedAtUtc);
        Assert.IsNull(session.EndedAtUtc);
        Assert.IsNull(session.FailureCode);
    }

    [TestMethod]
    public void CreatingWithoutARequestIsRejected()
    {
        var timeProvider = new FakeTimeProvider();

        Assert.ThrowsExactly<ArgumentNullException>(() => SessionReference.Create(
            new SessionReferenceId(Guid.CreateVersion7()),
            null!,
            SessionLifecyclePolicy.DisconnectOnWindowClose,
            timeProvider));
    }

    [TestMethod]
    public void ConnectingMovesToConnectedAndRecordsTheConnectedTime()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        session.Connect();

        Assert.AreEqual(SessionState.Connected, session.State);
        Assert.AreEqual(timeProvider.GetUtcNow(), session.ConnectedAtUtc);
    }

    [TestMethod]
    public void ReconnectingAfterADisconnectSucceeds()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();
        session.Disconnect();

        session.Connect();

        Assert.AreEqual(SessionState.Connected, session.State);
    }

    [TestMethod]
    public void ResumingASuspendedSessionByConnectingSucceeds()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();
        session.Suspend();

        session.Connect();

        Assert.AreEqual(SessionState.Connected, session.State);
    }

    [TestMethod]
    public void DisconnectingAConnectedSessionKeepsItAliveForReconnect()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();

        session.Disconnect();

        Assert.AreEqual(SessionState.Disconnected, session.State, "A disconnect must not end the session reference.");
        Assert.IsNull(session.EndedAtUtc);
    }

    [TestMethod]
    public void ADisconnectCanRecordAFailureCodeWithoutEndingTheSession()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();

        session.Disconnect(new SessionFailureCode("session.failure.connection_lost"));

        Assert.AreEqual(SessionState.Disconnected, session.State);
        Assert.AreEqual(new SessionFailureCode("session.failure.connection_lost"), session.FailureCode);
    }

    [TestMethod]
    public void ReconnectingClearsAPreviouslyRecordedFailureCode()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();
        session.Disconnect(new SessionFailureCode("session.failure.connection_lost"));

        session.Connect();

        Assert.IsNull(session.FailureCode);
    }

    [TestMethod]
    public void SuspendingAConnectedSessionKeepsItAliveToResume()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();

        session.Suspend();

        Assert.AreEqual(SessionState.Suspended, session.State);
        Assert.IsNull(session.EndedAtUtc);
    }

    [TestMethod]
    public void TerminatingEndsTheSessionAndRecordsTheEndedTime()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        session.Terminate();

        Assert.AreEqual(SessionState.Ended, session.State);
        Assert.AreEqual(timeProvider.GetUtcNow(), session.EndedAtUtc);
    }

    [TestMethod]
    public void TerminatingCanRecordAFailureCode()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);

        session.Terminate(new SessionFailureCode("session.failure.rejected"));

        Assert.AreEqual(new SessionFailureCode("session.failure.rejected"), session.FailureCode);
    }

    [TestMethod]
    public void ClosingTheWindowWithTheDisconnectPolicyDisconnectsRatherThanEndsTheSession()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider, SessionLifecyclePolicy.DisconnectOnWindowClose);
        session.Connect();

        session.ApplyWindowClosed();

        Assert.AreEqual(
            SessionState.Disconnected,
            session.State,
            "Window close and session termination must be distinct: this policy keeps the session alive.");
    }

    [TestMethod]
    public void ClosingTheWindowWithTheSuspendPolicySuspendsRatherThanEndsTheSession()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider, SessionLifecyclePolicy.SuspendOnWindowClose);
        session.Connect();

        session.ApplyWindowClosed();

        Assert.AreEqual(SessionState.Suspended, session.State);
        Assert.IsNull(session.EndedAtUtc);
    }

    [TestMethod]
    public void ClosingTheWindowWithTheTerminatePolicyEndsTheSession()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider, SessionLifecyclePolicy.TerminateOnWindowClose);
        session.Connect();

        session.ApplyWindowClosed();

        Assert.AreEqual(SessionState.Ended, session.State);
    }

    [TestMethod]
    public void ConnectingAnAlreadyConnectedSessionIsRejected()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Connect());

        Assert.AreEqual("session.transition.invalid", exception.Code);
    }

    [TestMethod]
    public void DisconnectingASessionThatNeverConnectedIsRejected()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Disconnect());

        Assert.AreEqual("session.transition.invalid", exception.Code);
    }

    [TestMethod]
    public void SuspendingASessionThatNeverConnectedIsRejected()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);

        Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Suspend());
    }

    [TestMethod]
    public void SuspendingADisconnectedSessionIsRejected()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Connect();
        session.Disconnect();

        Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Suspend());
    }

    [TestMethod]
    public void TerminatingAnAlreadyEndedSessionIsRejected()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Terminate();

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Terminate());

        Assert.AreEqual("session.transition.invalid", exception.Code);
    }

    [TestMethod]
    public void ConnectingAnEndedSessionIsRejected()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        session.Terminate();

        Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Connect());
    }

    [TestMethod]
    public void EveryAcceptedTransitionAdvancesTheRevision()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);
        Assert.AreEqual(Revision.Initial, session.Revision);

        session.Connect();
        Assert.AreEqual(Revision.From(2), session.Revision);

        session.Disconnect();
        Assert.AreEqual(Revision.From(3), session.Revision);

        session.Connect();
        Assert.AreEqual(Revision.From(4), session.Revision);

        session.Terminate();
        Assert.AreEqual(Revision.From(5), session.Revision);
    }

    [TestMethod]
    public void ARejectedTransitionDoesNotAdvanceTheRevision()
    {
        var timeProvider = new FakeTimeProvider();
        var session = ACreatedReference(timeProvider);

        Assert.ThrowsExactly<DomainRuleViolationException>(() => session.Disconnect());

        Assert.AreEqual(Revision.Initial, session.Revision);
    }
}
