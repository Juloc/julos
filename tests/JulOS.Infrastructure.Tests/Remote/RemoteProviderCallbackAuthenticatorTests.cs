using JulOS.Infrastructure.Remote;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.Tests.Remote;

[TestClass]
public sealed class RemoteProviderCallbackAuthenticatorTests
{
    private const string SigningKey = "test-only-provider-callback-signing-key-42";

    [TestMethod]
    public void TokenMatchesOnlyExactSessionRuntimeAndLifetime()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 20, 0, 0, TimeSpan.Zero));
        using var authenticator = Create(clock);
        var sessionId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        const string runtimeId = "remote-88888888888848888888888888888888";
        var token = authenticator.Issue(sessionId, runtimeId, clock.GetUtcNow().AddMinutes(5));

        Assert.IsTrue(authenticator.Authenticate(sessionId, runtimeId, token));
        Assert.IsFalse(authenticator.Authenticate(Guid.CreateVersion7(), runtimeId, token));
        Assert.IsFalse(authenticator.Authenticate(sessionId, "remote-other", token));

        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.IsFalse(authenticator.Authenticate(sessionId, runtimeId, token));
    }

    [TestMethod]
    public void MissingOrPartialConfigurationFailsClosed()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        using var missing = RemoteProviderCallbackAuthenticator.Read(
            new ConfigurationBuilder().Build(),
            clock);
        Assert.IsFalse(missing.IsConfigured);
        Assert.ThrowsExactly<RemoteProviderCallbackAuthenticationException>(() =>
            missing.Issue(Guid.CreateVersion7(), "remote-test", clock.GetUtcNow().AddMinutes(1)));

        var partial = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Remote:ProviderCallback:Endpoint"] =
                    "https://localhost/api/v1/internal/remote/provider-events",
            })
            .Build();
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            RemoteProviderCallbackAuthenticator.Read(partial, clock));
    }

    private static RemoteProviderCallbackAuthenticator Create(TimeProvider clock)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Remote:ProviderCallback:Endpoint"] =
                    "https://localhost/api/v1/internal/remote/provider-events",
                ["Remote:ProviderCallback:SigningKey"] = SigningKey,
            })
            .Build();
        return RemoteProviderCallbackAuthenticator.Read(configuration, clock);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this.utcNow;

        internal void Advance(TimeSpan duration) => this.utcNow = this.utcNow.Add(duration);
    }
}
