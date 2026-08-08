using JulOS.Browser.Worker;

namespace JulOS.Browser.Worker.Tests;

[TestClass]
public sealed class BrowserProfilePolicyTests
{
    [TestMethod]
    public void FromConfigurationParsesAllowedAndDefaultNetworks()
    {
        var policy = BrowserProfilePolicy.FromConfiguration(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["allowedNetworks"] = "julos-lan, julos-guest",
            ["defaultNetwork"] = "julos-lan",
        });

        Assert.AreEqual(2, policy.AllowedNetworkCount);
        Assert.AreEqual("julos-lan", policy.DefaultNetwork);
    }

    [TestMethod]
    public void FromConfigurationRejectsDefaultNetworkOutsideAllowlist()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BrowserProfilePolicy.FromConfiguration(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["allowedNetworks"] = "julos-lan",
                ["defaultNetwork"] = "julos-guest",
            }));
    }

    [TestMethod]
    public void FromConfigurationWithoutNetworksHasNoDefault()
    {
        var policy = BrowserProfilePolicy.FromConfiguration(new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.AreEqual(0, policy.AllowedNetworkCount);
        Assert.IsNull(policy.DefaultNetwork);
    }

    [TestMethod]
    public void CreateNetworkProfileRejectsRuntimeNetworkOutsideAllowlist()
    {
        var policy = new BrowserProfilePolicy(["julos-lan"], "julos-lan");

        Assert.ThrowsExactly<InvalidOperationException>(
            () => policy.CreateNetworkProfile("proxy", "julos-guest"));
    }

    [TestMethod]
    public void CreateNetworkProfileRejectsEmptySecretReference()
    {
        var policy = new BrowserProfilePolicy(["julos-lan"], "julos-lan");

        Assert.ThrowsExactly<ArgumentException>(
            () => policy.CreateNetworkProfile("proxy", "julos-lan", Guid.Empty));
    }

    [TestMethod]
    public void CreateNetworkProfileAcceptsAllowlistedNetwork()
    {
        var policy = new BrowserProfilePolicy(["julos-lan"], "julos-lan");
        var secretReference = Guid.NewGuid();

        var profile = policy.CreateNetworkProfile("proxy", "julos-lan", secretReference);

        Assert.AreEqual("proxy", profile.Key);
        Assert.AreEqual("julos-lan", profile.RuntimeNetwork);
        Assert.AreEqual(secretReference, profile.ProxySecretReferenceId);
    }

    [TestMethod]
    public void CreateProfileRejectsMissingOwner()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BrowserProfilePolicy.CreateProfile(
            Guid.Empty,
            "Work",
            BrowserProfileMode.Persistent,
            "julos-lan",
            startUrl: null,
            applicationKey: null,
            DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void CreateProfileApplicationModeRequiresStartUrlAndApplicationKey()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BrowserProfilePolicy.CreateProfile(
            Guid.NewGuid(),
            "Grafana",
            BrowserProfileMode.Application,
            "julos-lan",
            startUrl: null,
            applicationKey: "grafana",
            DateTimeOffset.UtcNow));

        Assert.ThrowsExactly<ArgumentException>(() => BrowserProfilePolicy.CreateProfile(
            Guid.NewGuid(),
            "Grafana",
            BrowserProfileMode.Application,
            "julos-lan",
            startUrl: new Uri("https://grafana.internal"),
            applicationKey: null,
            DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void CreateProfileRejectsApplicationKeyOutsideApplicationMode()
    {
        Assert.ThrowsExactly<ArgumentException>(() => BrowserProfilePolicy.CreateProfile(
            Guid.NewGuid(),
            "Work",
            BrowserProfileMode.Persistent,
            "julos-lan",
            startUrl: null,
            applicationKey: "grafana",
            DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public void CreateProfilePersistsValidatedApplicationProfile()
    {
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var profile = BrowserProfilePolicy.CreateProfile(
            owner,
            "Grafana",
            BrowserProfileMode.Application,
            "julos-lan",
            new Uri("https://grafana.internal"),
            "grafana",
            now);

        Assert.AreEqual(owner, profile.OwnerUserId);
        Assert.AreEqual(BrowserProfileMode.Application, profile.Mode);
        Assert.AreEqual("grafana", profile.ApplicationKey);
        Assert.AreEqual(1, profile.Revision);
    }

    [TestMethod]
    public void RuntimeStorageNeverPersistsTemporaryProfiles()
    {
        var profile = BrowserProfilePolicy.CreateProfile(
            Guid.NewGuid(),
            "Temporary",
            BrowserProfileMode.Temporary,
            "julos-lan",
            startUrl: null,
            applicationKey: null,
            DateTimeOffset.UtcNow);

        var storage = BrowserProfilePolicy.RuntimeStorage(profile);

        Assert.IsNull(storage.VolumeName);
        Assert.IsTrue(storage.DeleteOnTermination);
    }

    [TestMethod]
    public void RuntimeStorageIsDeterministicForTheSameProfileIdentity()
    {
        var owner = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var profile = new BrowserProfile(
            profileId,
            owner,
            "Work",
            BrowserProfileMode.Persistent,
            "julos-lan",
            StartUrl: null,
            ApplicationKey: null,
            now,
            now,
            Revision: 1);

        var first = BrowserProfilePolicy.RuntimeStorage(profile);
        var second = BrowserProfilePolicy.RuntimeStorage(profile);

        Assert.IsFalse(first.DeleteOnTermination);
        Assert.IsNotNull(first.VolumeName);
        Assert.AreEqual(first.VolumeName, second.VolumeName);
        Assert.IsTrue(first.VolumeName!.StartsWith("julos-de-juloc-julos-browser-profile-", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EnsureOwnerRejectsAnotherUser()
    {
        var profile = BrowserProfilePolicy.CreateProfile(
            Guid.NewGuid(),
            "Work",
            BrowserProfileMode.Persistent,
            "julos-lan",
            startUrl: null,
            applicationKey: null,
            DateTimeOffset.UtcNow);

        Assert.ThrowsExactly<UnauthorizedAccessException>(
            () => BrowserProfilePolicy.EnsureOwner(profile, Guid.NewGuid()));
    }

    [TestMethod]
    public void EnsureOwnerAcceptsTheOwningUser()
    {
        var owner = Guid.NewGuid();
        var profile = BrowserProfilePolicy.CreateProfile(
            owner,
            "Work",
            BrowserProfileMode.Persistent,
            "julos-lan",
            startUrl: null,
            applicationKey: null,
            DateTimeOffset.UtcNow);

        BrowserProfilePolicy.EnsureOwner(profile, owner);
    }
}
