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
        CollectionAssert.AreEqual(
            new[] { "julos-remote" },
            selection.NetworkProfile.RuntimeNetworks.ToArray());
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
