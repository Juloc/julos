using JulOS.Infrastructure.Remote;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Tests.Remote;

[TestClass]
public sealed class RemoteDisplayGatewayTests
{
    private const string CallerPackageId = "de.juloc.julos.remote";
    private const string RuntimeId = "remote-11111111111141118111111111111111";

    [TestMethod]
    public void IssueCreatesTokenFreeExactDescriptor()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 4, 20, 0, 0, TimeSpan.Zero));
        var gateway = CreateGateway(clock);
        var sessionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var ownerUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        var descriptor = gateway.Issue(
            sessionId,
            ownerUserId,
            CallerPackageId,
            RuntimeId,
            revision: 7,
            sessionExpiresAtUtc: clock.GetUtcNow().AddMinutes(10));

        Assert.AreEqual(RemoteDisplayGateway.DisplayKind, descriptor.Kind);
        Assert.AreEqual(RemoteDisplayGateway.ContractVersion, descriptor.ContractVersion);
        Assert.AreEqual(clock.GetUtcNow().AddSeconds(60), descriptor.ExpiresAtUtc);
        Assert.IsFalse(descriptor.Endpoint.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(descriptor.Endpoint.StartsWith(
            $"/api/v1/remote/sessions/{sessionId:D}/display?",
            StringComparison.Ordinal));
        Assert.IsTrue(descriptor.Endpoint.Contains(
            $"package={CallerPackageId}",
            StringComparison.Ordinal));
        Assert.IsTrue(descriptor.Endpoint.Contains("revision=7", StringComparison.Ordinal));

        var expires = descriptor.ExpiresAtUtc.ToUnixTimeSeconds();
        Assert.IsTrue(gateway.MatchesDescriptor(
            sessionId,
            ownerUserId,
            CallerPackageId,
            RuntimeId,
            revision: 7,
            expires,
            descriptor.Endpoint));
        Assert.IsFalse(gateway.MatchesDescriptor(
            sessionId,
            ownerUserId,
            "de.juloc.other",
            RuntimeId,
            revision: 7,
            expires,
            descriptor.Endpoint));
        Assert.IsFalse(gateway.MatchesDescriptor(
            sessionId,
            ownerUserId,
            CallerPackageId,
            RuntimeId,
            revision: 8,
            expires,
            descriptor.Endpoint));

        Assert.IsTrue(gateway.IsAllowedOrigin("https://os.example.test"));
        Assert.IsFalse(gateway.IsAllowedOrigin("https://attacker.example"));
        Assert.AreEqual(
            $"ws://provider.test/runtime/{RuntimeId}",
            gateway.ProviderEndpoint(RuntimeId).AbsoluteUri);
    }

    [TestMethod]
    public void DescriptorIsBoundedBySessionAndClock()
    {
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 4, 21, 0, 0, TimeSpan.Zero));
        var gateway = CreateGateway(clock);
        var descriptor = gateway.Issue(
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            CallerPackageId,
            RuntimeId,
            revision: 2,
            sessionExpiresAtUtc: clock.GetUtcNow().AddSeconds(20));

        Assert.AreEqual(clock.GetUtcNow().AddSeconds(20), descriptor.ExpiresAtUtc);
        clock.Advance(TimeSpan.FromSeconds(21));

        Assert.IsFalse(gateway.MatchesDescriptor(
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            CallerPackageId,
            RuntimeId,
            revision: 2,
            descriptor.ExpiresAtUtc.ToUnixTimeSeconds(),
            descriptor.Endpoint));
    }

    [TestMethod]
    public void ConfigurationRejectsUnsafeOriginsAndProviderTemplates()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RemoteDisplayGateway.Read(
                Configuration(
                    ("Remote:Display:ProviderEndpointTemplate", "http://provider/{runtimeId}"),
                    ("Remote:Display:PublicOrigin", "https://os.example.test")),
                clock));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RemoteDisplayGateway.Read(
                Configuration(
                    ("Remote:Display:ProviderEndpointTemplate", "ws://provider/{runtimeId}"),
                    ("Remote:Display:PublicOrigin", "https://os.example.test/path")),
                clock));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RemoteDisplayGateway.Read(
                Configuration(
                    ("Remote:Display:ProviderEndpointTemplate", "ws://provider/{runtimeId}"),
                    ("Remote:Display:PublicOrigin", "https://os.example.test"),
                    ("Remote:Display:GrantLifetimeSeconds", "301")),
                clock));
    }

    private static RemoteDisplayGateway CreateGateway(TimeProvider clock) =>
        RemoteDisplayGateway.Read(
            Configuration(
                ("Remote:Display:ProviderEndpointTemplate", "ws://provider.test/runtime/{runtimeId}"),
                ("Remote:Display:PublicOrigin", "https://os.example.test"),
                ("Remote:Display:GrantLifetimeSeconds", "60")),
            clock);

    private static IConfiguration Configuration(
        params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(item => item.Key, item => item.Value))
            .Build();

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this.utcNow;

        internal void Advance(TimeSpan duration) => this.utcNow = this.utcNow.Add(duration);
    }
}
