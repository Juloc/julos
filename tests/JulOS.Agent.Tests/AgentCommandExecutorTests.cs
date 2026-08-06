using System.Text.Json;

using JulOS.Agent;
using JulOS.Contracts.Agents;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentCommandExecutorTests
{
    [TestMethod]
    public async Task DiagnosticsSnapshotReturnsBoundedTypedResult()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 2, 22, 0, 0, TimeSpan.Zero));
        var diagnostics = new AgentRuntimeDiagnostics(clock.GetUtcNow());
        diagnostics.RecordConnectionAttempt();
        diagnostics.RecordHeartbeatSucceeded(clock.GetUtcNow());
        var executor = new AgentCommandExecutor(
            clock,
            "1.0.0",
            new AgentCapabilityInventory(),
            diagnostics);

        var result = await executor.ExecuteAsync(
            Command("diagnostics.snapshot", clock.GetUtcNow().AddMinutes(1)),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.ErrorCode);
        Assert.AreEqual("1.0.0", result.Result.GetProperty("version").GetString());
        Assert.AreEqual(
            AgentProtocolContract.CurrentVersion,
            result.Result.GetProperty("protocolVersion").GetInt32());
        Assert.AreEqual(clock.GetUtcNow(), result.Result.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.AreEqual(clock.GetUtcNow(), result.Result.GetProperty("observedAtUtc").GetDateTimeOffset());
        Assert.IsTrue(result.Result.GetProperty("capabilities").GetArrayLength() >= 3);
        Assert.AreEqual(
            1,
            result.Result.GetProperty("reconnect").GetProperty("connectionAttempts").GetInt32());
        Assert.AreEqual(
            AgentUpdateContract.CurrentVersion,
            result.Result.GetProperty("updateContract").GetProperty("contractVersion").GetInt32());
        Assert.IsFalse(
            result.Result.GetProperty("updateContract").GetProperty("automaticApplySupported").GetBoolean());
    }

    [TestMethod]
    public async Task UnknownCommandCannotExecuteArbitraryWork()
    {
        var clock = new FakeTimeProvider();
        var executor = new AgentCommandExecutor(clock, "1.0.0");

        var result = await executor.ExecuteAsync(
            Command("shell.execute", clock.GetUtcNow().AddMinutes(1)),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("agent.command_not_supported", result.ErrorCode);
    }

    [TestMethod]
    public async Task ExpiredCommandIsNotExecuted()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 2, 22, 0, 0, TimeSpan.Zero));
        var executor = new AgentCommandExecutor(clock, "1.0.0");

        var result = await executor.ExecuteAsync(
            Command("diagnostics.snapshot", clock.GetUtcNow().AddSeconds(-1)),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("agent.command_expired", result.ErrorCode);
    }

    private static AgentCommandResponse Command(string commandType, DateTimeOffset expiresAt) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "test-operation",
        commandType,
        JsonSerializer.SerializeToElement(new { }),
        "running",
        expiresAt.AddMinutes(-1),
        expiresAt,
        expiresAt.AddSeconds(-30),
        null,
        null,
        null,
        2);
}
