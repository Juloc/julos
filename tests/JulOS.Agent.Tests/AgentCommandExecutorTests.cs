using System.Text.Json;

using JulOS.Agent;
using JulOS.Contracts.Agents;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentCommandExecutorTests
{
    [TestMethod]
    public async Task PingCommandReturnsBoundedTypedResult()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 2, 22, 0, 0, TimeSpan.Zero));
        var executor = new AgentCommandExecutor(clock, "1.0.0");

        var result = await executor.ExecuteAsync(Command("agent.ping", clock.GetUtcNow().AddMinutes(1)), CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.ErrorCode);
        Assert.IsTrue(result.Result.GetProperty("pong").GetBoolean());
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
            Command("agent.ping", clock.GetUtcNow().AddSeconds(-1)),
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
