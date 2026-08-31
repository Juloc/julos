using JulOS.Application.Remote;
using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;
using JulOS.Infrastructure.Remote;

namespace JulOS.Infrastructure.Tests.Remote;

[TestClass]
public sealed class ConfiguredRemoteRuntimePolicyTests
{
    private static readonly Guid NetworkProfileId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    private const string ProviderImage =
        "ghcr.io/juloc/julos-remote@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BrowserProviderImage =
        "ghcr.io/juloc/julos-adaptive-browser-provider@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [TestMethod]
    public void AllowlistedProviderTargetPortAndNetworkResolveExactly()
    {
        var policy = CreatePolicy();

        var selection = policy.Resolve(
            "rdp",
            NetworkProfileId,
            new RemoteTargetContract("node.example.test", 3389));

        Assert.AreEqual("de.juloc.julos.remote-provider", selection.Provider.ProviderPackageId);
        Assert.AreEqual("1.0.0", selection.Provider.PackageVersion);
        Assert.AreEqual(ProviderImage, selection.Provider.Image);
        Assert.AreEqual(1, selection.NetworkProfile.RuntimeNetworks.Count);
        Assert.AreEqual("julos-remote", selection.NetworkProfile.RuntimeNetworks[0]);
    }

    [TestMethod]
    public void BrowserStreamSelectsItsOwnProviderWithoutChangingRemoteProtocols()
    {
        var browserProvider = new RemoteProviderRuntimeDefinition(
            "browser-stream",
            "de.juloc.julos.adaptive-browser-provider",
            "0.1.0",
            BrowserProviderImage,
            new RuntimeResourceLimits(256, 1m, 128));
        var network = NetworkProfile() with
        {
            AllowedTargetPatterns = ["julos-interactive-*"],
            AllowedPorts = [8080, 3389],
        };
        var policy = new ConfiguredRemoteRuntimePolicy([Provider(), browserProvider], [network]);

        var selection = policy.Resolve(
            "browser-stream",
            NetworkProfileId,
            new RemoteTargetContract("julos-interactive-a1b2c3", 8080));

        Assert.AreEqual("de.juloc.julos.adaptive-browser-provider", selection.Provider.ProviderPackageId);
        Assert.AreEqual(BrowserProviderImage, selection.Provider.Image);
        Assert.AreEqual("browser-stream", selection.Provider.Protocol);
        Assert.AreEqual("de.juloc.julos.remote-provider", policy.Resolve(
            "rdp",
            NetworkProfileId,
            new RemoteTargetContract("julos-interactive-a1b2c3", 3389)).Provider.ProviderPackageId);
    }

    [TestMethod]
    public void DynamicRuntimePrefixMatchesOnlyItsOwnNames()
    {
        var profile = NetworkProfile() with
        {
            AllowedTargetPatterns = ["julos-interactive-*"],
        };
        var policy = new ConfiguredRemoteRuntimePolicy([Provider()], [profile]);

        _ = policy.Resolve(
            "rdp",
            NetworkProfileId,
            new RemoteTargetContract("julos-interactive-a1b2c3", 3389));

        var failure = Assert.ThrowsExactly<RemoteRuntimePolicyException>(() =>
            policy.Resolve(
                "rdp",
                NetworkProfileId,
                new RemoteTargetContract("other-interactive-a1b2c3", 3389)));
        Assert.AreEqual(RemoteSessionFailureCodes.TargetInvalid, failure.Code);
    }

    [TestMethod]
    public void UnknownProtocolFailsClosed()
    {
        var failure = Assert.ThrowsExactly<RemoteRuntimePolicyException>(() =>
            CreatePolicy().Resolve(
                "vnc",
                NetworkProfileId,
                new RemoteTargetContract("node.example.test", 3389)));

        Assert.AreEqual(RemoteSessionFailureCodes.ProtocolUnsupported, failure.Code);
    }

    [TestMethod]
    [DataRow("example.test", 3389)]
    [DataRow("node.example.test", 22)]
    [DataRow("outside.invalid", 3389)]
    public void TargetMustMatchBothHostPatternAndPort(string host, int port)
    {
        var failure = Assert.ThrowsExactly<RemoteRuntimePolicyException>(() =>
            CreatePolicy().Resolve(
                "rdp",
                NetworkProfileId,
                new RemoteTargetContract(host, port)));

        Assert.AreEqual(RemoteSessionFailureCodes.TargetInvalid, failure.Code);
    }

    [TestMethod]
    public void MissingNetworkProfileFailsClosed()
    {
        var failure = Assert.ThrowsExactly<RemoteRuntimePolicyException>(() =>
            CreatePolicy().Resolve(
                "rdp",
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                new RemoteTargetContract("node.example.test", 3389)));

        Assert.AreEqual(RemoteSessionFailureCodes.NetworkProfileUnavailable, failure.Code);
    }

    [TestMethod]
    public void MovingProviderImageTagIsRejectedDuringConfiguration()
    {
        var provider = Provider() with { Image = "ghcr.io/juloc/julos-remote:latest" };

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            _ = new ConfiguredRemoteRuntimePolicy([provider], [NetworkProfile()]));

        StringAssert.Contains(failure.Message, "digest-pinned", StringComparison.Ordinal);
    }

    private static ConfiguredRemoteRuntimePolicy CreatePolicy() =>
        new([Provider()], [NetworkProfile()]);

    private static RemoteProviderRuntimeDefinition Provider() => new(
        "rdp",
        "de.juloc.julos.remote-provider",
        "1.0.0",
        ProviderImage,
        new RuntimeResourceLimits(256, 1m, 128));

    private static RemoteNetworkProfileDefinition NetworkProfile() => new(
        NetworkProfileId,
        Default: true,
        RuntimeNetworks: ["julos-remote"],
        AllowedTargetPatterns: ["*.example.test"],
        AllowedPorts: [3389]);
}
