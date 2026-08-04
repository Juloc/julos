using JulOS.RuntimeManager;

namespace JulOS.Infrastructure.Tests.RuntimeManager;

[TestClass]
public sealed class RuntimePolicyTests
{
    private const string Image = "ghcr.io/juloc/test@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public void PackageOwnedPinnedRuntimeIsAccepted()
    {
        var policy = new RuntimePolicy(["julos-runtime"]);
        var request = ValidRequest();

        policy.Validate(request);
    }

    [TestMethod]
    public void MovingImageTagIsRejected()
    {
        var policy = new RuntimePolicy(["julos-runtime"]);
        var failure = Assert.ThrowsExactly<RuntimeManagerException>(() => policy.Validate(
            ValidRequest() with { Image = "ghcr.io/juloc/test:latest" }));

        Assert.AreEqual("runtime.image.unpinned", failure.Code);
    }

    [TestMethod]
    public void HostNetworkAndUnknownNetworksAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _ = new RuntimePolicy(["host"]));
        var policy = new RuntimePolicy(["julos-runtime"]);
        var failure = Assert.ThrowsExactly<RuntimeManagerException>(() => policy.Validate(
            ValidRequest() with { Networks = ["unrelated-network"] }));

        Assert.AreEqual("runtime.network.denied", failure.Code);
    }

    [TestMethod]
    public void BindMountAndForeignNamedVolumeCannotBeRequested()
    {
        var policy = new RuntimePolicy(["julos-runtime"]);
        var foreign = Assert.ThrowsExactly<RuntimeManagerException>(() => policy.Validate(
            ValidRequest() with
            {
                Volumes = [new RuntimeVolumeRequest("foreign-data", "/data", false)],
            }));
        var traversal = Assert.ThrowsExactly<RuntimeManagerException>(() => policy.Validate(
            ValidRequest() with
            {
                Volumes = [new RuntimeVolumeRequest("julos-de-juloc-test-data", "/data/../host", false)],
            }));

        Assert.AreEqual("runtime.volume.denied", foreign.Code);
        Assert.AreEqual("runtime.volume.target_invalid", traversal.Code);
    }

    [TestMethod]
    public void SecretLikeEnvironmentVariablesAreRejected()
    {
        var policy = new RuntimePolicy(["julos-runtime"]);
        var failure = Assert.ThrowsExactly<RuntimeManagerException>(() => policy.Validate(
            ValidRequest() with
            {
                Environment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["DATABASE_PASSWORD"] = "must-use-secret-lease",
                },
            }));

        Assert.AreEqual("runtime.environment.invalid", failure.Code);
    }

    [TestMethod]
    public void InvalidProcessLimitIsRejected()
    {
        var policy = new RuntimePolicy(["julos-runtime"]);
        var failure = Assert.ThrowsExactly<RuntimeManagerException>(() => policy.Validate(
            ValidRequest() with { PidsLimit = 0 }));

        Assert.AreEqual("runtime.pids.invalid", failure.Code);
    }

    private static RuntimeCreateRequest ValidRequest() =>
        new(
            "test-runtime",
            "de.juloc.test",
            "1.0.0",
            "test-runtime",
            Image,
            1m,
            256,
            128,
            ["julos-runtime"],
            [new RuntimeVolumeRequest("julos-de-juloc-test-data", "/data", false)],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["JULOS_MODE"] = "worker",
            });
}
