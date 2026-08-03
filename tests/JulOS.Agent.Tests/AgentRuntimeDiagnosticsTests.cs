using JulOS.Agent;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentRuntimeDiagnosticsTests
{
    [TestMethod]
    public void SuccessfulHeartbeatResetsOnlyCurrentFailureState()
    {
        var startedAt = new DateTimeOffset(2026, 8, 3, 20, 0, 0, TimeSpan.Zero);
        var diagnostics = new AgentRuntimeDiagnostics(startedAt);
        diagnostics.RecordConnectionAttempt();
        diagnostics.RecordConnectionFailure(
            startedAt.AddSeconds(1),
            "transport",
            TimeSpan.FromSeconds(4));

        diagnostics.RecordHeartbeatSucceeded(startedAt.AddSeconds(5));
        var snapshot = diagnostics.Snapshot();

        Assert.AreEqual(1, snapshot.ConnectionAttempts);
        Assert.AreEqual(1, snapshot.SuccessfulHeartbeats);
        Assert.AreEqual(0, snapshot.ConsecutiveFailures);
        Assert.AreEqual(startedAt.AddSeconds(5), snapshot.LastConnectedAtUtc);
        Assert.AreEqual(startedAt.AddSeconds(1), snapshot.LastFailureAtUtc);
        Assert.AreEqual("transport", snapshot.LastFailureKind);
        Assert.IsNull(snapshot.NextRetryDelaySeconds);
    }

    [TestMethod]
    public void FailureKindAndRetryDelayAreBounded()
    {
        var diagnostics = new AgentRuntimeDiagnostics(DateTimeOffset.UtcNow);

        Assert.ThrowsExactly<ArgumentException>(() => diagnostics.RecordConnectionFailure(
            DateTimeOffset.UtcNow,
            " invalid ",
            TimeSpan.FromSeconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => diagnostics.RecordConnectionFailure(
            DateTimeOffset.UtcNow,
            "transport",
            TimeSpan.FromMinutes(6)));
    }
}
