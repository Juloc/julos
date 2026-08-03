using JulOS.Agent;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class LinuxMetricsCollectorTests
{
    private string? temporaryDirectory;

    [TestCleanup]
    public void Cleanup()
    {
        if (this.temporaryDirectory is not null && Directory.Exists(this.temporaryDirectory))
        {
            Directory.Delete(this.temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CpuUtilizationRequiresTwoObservationsAndPreservesTimestamp()
    {
        var directory = this.CreateDirectory();
        var stat = Path.Combine(directory, "stat");
        var memory = Path.Combine(directory, "meminfo");
        var load = Path.Combine(directory, "loadavg");
        await File.WriteAllTextAsync(stat, "cpu  100 0 100 800 0 0 0 0 0 0\n");
        await File.WriteAllTextAsync(memory, "MemTotal: 1000 kB\nMemAvailable: 400 kB\n");
        await File.WriteAllTextAsync(load, "1.00 0.50 0.25 1/100 1\n");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 2, 22, 0, 0, TimeSpan.Zero));
        var collector = new LinuxMetricsCollector(clock, stat, memory, load, directory);

        var first = await collector.CollectAsync(CancellationToken.None);
        Assert.IsNull(first.Single(metric => metric.Name == "host.cpu.utilization").Value);

        clock.Advance(TimeSpan.FromSeconds(10));
        await File.WriteAllTextAsync(stat, "cpu  150 0 150 900 0 0 0 0 0 0\n");
        var second = await collector.CollectAsync(CancellationToken.None);
        var cpu = second.Single(metric => metric.Name == "host.cpu.utilization");

        Assert.IsNotNull(cpu.Value);
        Assert.AreEqual(0.5d, cpu.Value.Value, 0.0001d);
        Assert.AreEqual(clock.GetUtcNow(), cpu.ObservedAtUtc);
        Assert.AreEqual(1024000d, second.Single(metric => metric.Name == "host.memory.total_bytes").Value);
        Assert.AreEqual(614400d, second.Single(metric => metric.Name == "host.memory.used_bytes").Value);
    }

    [TestMethod]
    public async Task MissingKernelFilesProduceUnknownRatherThanZero()
    {
        var directory = this.CreateDirectory();
        var collector = new LinuxMetricsCollector(
            new FakeTimeProvider(),
            Path.Combine(directory, "missing-stat"),
            Path.Combine(directory, "missing-memory"),
            Path.Combine(directory, "missing-load"),
            Path.Combine(directory, "missing-disk"));

        var metrics = await collector.CollectAsync(CancellationToken.None);

        Assert.IsNull(metrics.Single(metric => metric.Name == "host.cpu.utilization").Value);
        Assert.IsNull(metrics.Single(metric => metric.Name == "host.memory.total_bytes").Value);
        Assert.IsNull(metrics.Single(metric => metric.Name == "host.load.one").Value);
        Assert.IsNull(metrics.Single(metric => metric.Name == "host.disk.total_bytes").Value);
    }

    private string CreateDirectory()
    {
        this.temporaryDirectory = Path.Combine(Path.GetTempPath(), $"julos-agent-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.temporaryDirectory);
        return this.temporaryDirectory;
    }
}
