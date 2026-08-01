using JulOS.Domain;
using JulOS.Domain.Agents;
using JulOS.Domain.Primitives;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Domain.Tests.Agents;

/// <summary>Verifies Agent enrollment, connection lifecycle and revocation.</summary>
[TestClass]
public sealed class AgentTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void EnrollmentStartsEnrolledAtTheInitialRevisionWithNoHeartbeat()
    {
        var agent = NewAgent(new FakeTimeProvider(Start));

        Assert.AreEqual(AgentConnectionState.Enrolled, agent.State);
        Assert.AreEqual(Revision.Initial, agent.Revision);
        Assert.AreEqual(Start, agent.EnrolledAtUtc);
        Assert.IsNull(agent.LastSeen);
        Assert.IsNull(agent.RevokedAtUtc);
    }

    [TestMethod]
    public void ConnectingMovesToConnectedAndRecordsAHeartbeat()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);

        agent.Connect(timeProvider);

        Assert.AreEqual(AgentConnectionState.Connected, agent.State);
        Assert.AreEqual(Start, agent.LastSeen!.Value.AtUtc);
    }

    [TestMethod]
    public void DisconnectingAndReconnectingSucceeds()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);
        agent.Connect(timeProvider);

        agent.Disconnect();
        Assert.AreEqual(AgentConnectionState.Disconnected, agent.State);

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        agent.Connect(timeProvider);

        Assert.AreEqual(AgentConnectionState.Connected, agent.State);
        Assert.AreEqual(Start.AddMinutes(10), agent.LastSeen!.Value.AtUtc);
    }

    [TestMethod]
    public void HeartbeatAdvancesLastSeenWithoutChangingState()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);
        agent.Connect(timeProvider);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        agent.Heartbeat(timeProvider);

        Assert.AreEqual(AgentConnectionState.Connected, agent.State);
        Assert.AreEqual(Start.AddSeconds(30), agent.LastSeen!.Value.AtUtc);
    }

    [TestMethod]
    public void AHeartbeatWhileNotConnectedFailsExplicitly()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => agent.Heartbeat(timeProvider));

        Assert.AreEqual("agent.heartbeat.not_connected", exception.Code);
    }

    [TestMethod]
    public void ARevokedAgentCannotTransitionToConnected()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);
        agent.Connect(timeProvider);
        agent.Disconnect();

        agent.Revoke(timeProvider);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => agent.Connect(timeProvider));

        Assert.AreEqual("agent.revoked", exception.Code);
        Assert.AreEqual(AgentConnectionState.Revoked, agent.State, "A refused transition must not change the state.");
    }

    [TestMethod]
    public void RevokingAnEnrolledAgentThatNeverConnectedSucceeds()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);

        agent.Revoke(timeProvider);

        Assert.AreEqual(AgentConnectionState.Revoked, agent.State);
        Assert.AreEqual(Start, agent.RevokedAtUtc);
    }

    [TestMethod]
    public void RevokingAnAlreadyRevokedAgentFailsExplicitly()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);
        agent.Revoke(timeProvider);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => agent.Revoke(timeProvider));

        Assert.AreEqual("agent.revoked", exception.Code);
    }

    [TestMethod]
    public void DisconnectingWithoutAnActiveConnectionFailsExplicitly()
    {
        var agent = NewAgent(new FakeTimeProvider(Start));

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(agent.Disconnect);

        Assert.AreEqual("agent.transition.invalid", exception.Code);
    }

    [TestMethod]
    public void RenamingTheAgentDoesNotChangeItsIdentity()
    {
        var agent = NewAgent(new FakeTimeProvider(Start));
        var id = agent.Id;
        var machineIdentity = agent.MachineIdentity;

        agent.Rename("A completely different label");

        Assert.AreEqual(id, agent.Id);
        Assert.AreEqual(machineIdentity, agent.MachineIdentity);
        Assert.AreEqual("A completely different label", agent.Name);
    }

    [TestMethod]
    public void ReportingPlatformOnReconnectRefreshesTheReportedValues()
    {
        var agent = NewAgent(new FakeTimeProvider(Start));

        agent.ReportPlatform("linux", "arm64", "1.2.3");

        Assert.AreEqual("linux", agent.OperatingSystem);
        Assert.AreEqual("arm64", agent.Architecture);
        Assert.AreEqual("1.2.3", agent.Version);
    }

    [TestMethod]
    public void ABlankNameIsRejected()
    {
        var timeProvider = new FakeTimeProvider(Start);

        var exception = Assert.ThrowsExactly<DomainRuleViolationException>(() => Agent.Enroll(
            new AgentId(Guid.CreateVersion7()),
            AgentMachineIdentity.Parse("host-1"),
            "   ",
            "linux",
            "x64",
            "1.0.0",
            timeProvider));

        Assert.AreEqual("agent.name.invalid", exception.Code);
    }

    [TestMethod]
    public void EveryAcceptedTransitionMovesTheRevisionForward()
    {
        var timeProvider = new FakeTimeProvider(Start);
        var agent = NewAgent(timeProvider);
        var revision = agent.Revision;

        agent.Connect(timeProvider);
        Assert.IsTrue(agent.Revision > revision);
        revision = agent.Revision;

        agent.Disconnect();
        Assert.IsTrue(agent.Revision > revision);
        revision = agent.Revision;

        agent.Revoke(timeProvider);
        Assert.IsTrue(agent.Revision > revision);
    }

    private static Agent NewAgent(TimeProvider timeProvider) => Agent.Enroll(
        new AgentId(Guid.CreateVersion7()),
        AgentMachineIdentity.Parse("host-1"),
        "Example host",
        "linux",
        "x64",
        "1.0.0",
        timeProvider);
}
