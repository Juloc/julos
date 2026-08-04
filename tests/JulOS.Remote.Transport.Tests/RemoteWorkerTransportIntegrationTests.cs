using JulOS.PackageSdk;
using JulOS.Remote.Transport;
using JulOS.Remote.Worker;

namespace JulOS.Remote.Transport.Tests;

[TestClass]
public sealed class RemoteWorkerTransportIntegrationTests
{
    [TestMethod]
    public async Task HealthReportsSharedProtocolCatalogSize()
    {
        var observedAt = new DateTimeOffset(2026, 8, 4, 6, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(observedAt);
        var worker = new RemoteWorker(timeProvider);
        var context = new PackageWorkerContext(
            "de.juloc.julos.remote",
            "0.1.0",
            new Uri("https://julos.example.test"),
            "remote-worker-01",
            new Dictionary<string, string>(StringComparer.Ordinal),
            Array.Empty<string>());

        await worker.ConfigureAsync(context, CancellationToken.None);
        await worker.StartAsync(CancellationToken.None);
        var health = await worker.ReadHealthAsync(CancellationToken.None);

        Assert.AreEqual("healthy", health.Status);
        Assert.AreEqual(observedAt, health.ObservedAtUtc);
        Assert.AreEqual(
            RemoteTransportProtocols.All.Count,
            health.Measurements["supportedProtocolCount"]);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.utcNow;
    }
}
