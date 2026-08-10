using System.Text.Json;

using JulOS.Browser.Worker;
using JulOS.PackageSdk;

namespace JulOS.Browser.Worker.Tests;

[TestClass]
public sealed class BrowserWorkerCommandTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string databasePath = string.Empty;
    private string connectionString = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        this.databasePath = Path.Combine(Path.GetTempPath(), $"julos-browser-worker-tests-{Guid.NewGuid():N}.db");
        this.connectionString = $"Data Source={this.databasePath};Pooling=False";
        Environment.SetEnvironmentVariable("JULOS_PACKAGE_DATABASE_PROVIDER", "sqlite");
        Environment.SetEnvironmentVariable("JULOS_PACKAGE_DATABASE", this.connectionString);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("JULOS_PACKAGE_DATABASE_PROVIDER", null);
        Environment.SetEnvironmentVariable("JULOS_PACKAGE_DATABASE", null);
        if (File.Exists(this.databasePath))
        {
            File.Delete(this.databasePath);
        }
    }

    [TestMethod]
    public async Task UnsupportedCommandNameFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(new PackageWorkerCommand("unknown", EmptyPayload()), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.command_unsupported", result.ErrorCode);
    }

    [TestMethod]
    public async Task InvalidRequestPayloadFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(ResolvePlanCommand(Guid.Empty, "https://example.test", "temporary", null), CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.request_invalid", result.ErrorCode);
    }

    [TestMethod]
    public async Task NonHttpUrlFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "file:///etc/passwd", "temporary", null),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.url_invalid", result.ErrorCode);
    }

    [TestMethod]
    public async Task MissingRuntimeImageFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", runtimeImage: null);

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "https://example.test", "temporary", null),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.runtime_not_configured", result.ErrorCode);
    }

    [TestMethod]
    public async Task TemporaryModeWithoutDefaultNetworkFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "https://example.test", "temporary", null),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.profile_invalid", result.ErrorCode);
    }

    [TestMethod]
    public async Task TemporaryModeSucceedsWithoutAPersistentVolume()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "https://example.test", "temporary", null),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        var plan = result.Payload.Deserialize<InteractiveSessionRuntimePlan>(JsonOptions);
        Assert.IsNotNull(plan);
        Assert.AreEqual("julos-lan", plan!.RuntimeNetwork);
        Assert.AreEqual(0, plan.Volumes.Count);
    }

    [TestMethod]
    public async Task RetainedModeWithoutProfileIdFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "https://example.test", "persistent", null),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.profile_invalid", result.ErrorCode);
    }

    [TestMethod]
    public async Task UnknownProfileFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "https://example.test", "persistent", Guid.NewGuid()),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.profile_not_found", result.ErrorCode);
    }

    [TestMethod]
    public async Task AnotherUsersProfileIsNotFound()
    {
        var owner = Guid.NewGuid();
        await SeedNetworkProfileAsync("julos-lan", "julos-lan");
        var profile = await SeedPersistentProfileAsync(owner, "julos-lan");
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(Guid.NewGuid(), "https://example.test", "persistent", profile.ProfileId),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.profile_not_found", result.ErrorCode);
    }

    [TestMethod]
    public async Task ProfileModeMismatchFails()
    {
        var owner = Guid.NewGuid();
        await SeedNetworkProfileAsync("julos-lan", "julos-lan");
        var profile = await SeedPersistentProfileAsync(owner, "julos-lan");
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(owner, "https://example.test", "application", profile.ProfileId),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.profile_mode_mismatch", result.ErrorCode);
    }

    [TestMethod]
    public async Task NetworkNoLongerAllowlistedIsDenied()
    {
        var owner = Guid.NewGuid();
        await SeedNetworkProfileAsync("julos-lan", "julos-lan");
        var profile = await SeedPersistentProfileAsync(owner, "julos-lan");

        // The administrator narrowed the allowlist after the network profile was created.
        var worker = await StartWorkerAsync(["julos-guest"], "julos-guest", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(owner, "https://example.test", "persistent", profile.ProfileId),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.network_denied", result.ErrorCode);
    }

    [TestMethod]
    public async Task RetainedModeSucceedsWithADeterministicVolume()
    {
        var owner = Guid.NewGuid();
        await SeedNetworkProfileAsync("julos-lan", "julos-lan");
        var profile = await SeedPersistentProfileAsync(owner, "julos-lan");
        var worker = await StartWorkerAsync(["julos-lan"], "julos-lan", "registry.example.test/browser@sha256:" + new string('a', 64));

        var result = await worker.InvokeCommandAsync(
            ResolvePlanCommand(owner, "https://example.test/ignored", "persistent", profile.ProfileId),
            CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        var plan = result.Payload.Deserialize<InteractiveSessionRuntimePlan>(JsonOptions);
        Assert.IsNotNull(plan);
        Assert.AreEqual("julos-lan", plan!.RuntimeNetwork);
        Assert.AreEqual(1, plan.Volumes.Count);
        Assert.IsTrue(plan.Volumes[0].Name.StartsWith("julos-de-juloc-julos-browser-profile-", StringComparison.Ordinal));
    }

    private static async Task<BrowserWorker> StartWorkerAsync(
        string[] allowedNetworks,
        string? defaultNetwork,
        string? runtimeImage)
    {
        var configuration = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["allowedNetworks"] = string.Join(',', allowedNetworks),
        };
        if (defaultNetwork is not null)
        {
            configuration["defaultNetwork"] = defaultNetwork;
        }
        if (runtimeImage is not null)
        {
            configuration["runtimeImage"] = runtimeImage;
        }

        var worker = new BrowserWorker(TimeProvider.System);
        var context = new PackageWorkerContext(
            "de.juloc.julos.browser",
            "1.0.0",
            new Uri("https://julos.internal.test"),
            "test-instance",
            configuration,
            []);
        await worker.ConfigureAsync(context, CancellationToken.None);
        await worker.StartAsync(CancellationToken.None);
        return worker;
    }

    private async Task<BrowserProfile> SeedPersistentProfileAsync(Guid owner, string networkProfileKey)
    {
        var store = new BrowserProfileStore("sqlite", this.connectionString);
        await store.InitializeAsync(CancellationToken.None);
        var profile = BrowserProfilePolicy.CreateProfile(
            owner,
            "Work",
            BrowserProfileMode.Persistent,
            networkProfileKey,
            startUrl: null,
            applicationKey: null,
            DateTimeOffset.UtcNow);
        await store.CreateProfileAsync(profile, CancellationToken.None);
        return profile;
    }

    private async Task SeedNetworkProfileAsync(string key, string runtimeNetwork)
    {
        var store = new BrowserProfileStore("sqlite", this.connectionString);
        await store.InitializeAsync(CancellationToken.None);
        await store.CreateNetworkProfileAsync(
            new BrowserNetworkProfile(key, runtimeNetwork, ProxySecretReferenceId: null, Revision: 1),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task CreateNetworkProfileStoresAndListsWithoutSecret()
    {
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);
        var secret = Guid.NewGuid();

        var created = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.CreateNetworkProfile,
                Guid.NewGuid(),
                new { key = "lan", runtimeNetwork = "julos-lan", proxySecretReferenceId = secret }),
            CancellationToken.None);

        Assert.IsTrue(created.Succeeded, created.ErrorCode);
        Assert.IsTrue(created.Payload.GetProperty("hasProxy").GetBoolean());

        var list = await worker.InvokeCommandAsync(
            ManageCommand(InteractiveProfilesWorkerCommands.ListNetworkProfiles, Guid.NewGuid(), request: null),
            CancellationToken.None);

        Assert.IsTrue(list.Succeeded, list.ErrorCode);
        var networks = list.Payload.GetProperty("networkProfiles");
        Assert.AreEqual(1, networks.GetArrayLength());
        Assert.AreEqual("lan", networks[0].GetProperty("key").GetString());
        Assert.IsFalse(
            list.Payload.GetRawText().Contains(secret.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "The proxy secret reference must never be returned.");
    }

    [TestMethod]
    public async Task CreateNetworkProfileOnUnlistedNetworkIsDenied()
    {
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);

        var result = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.CreateNetworkProfile,
                Guid.NewGuid(),
                new { key = "guest", runtimeNetwork = "julos-guest" }),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.network_denied", result.ErrorCode);
    }

    [TestMethod]
    public async Task CreateNetworkProfileRejectsADuplicateKey()
    {
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);
        var command = ManageCommand(
            InteractiveProfilesWorkerCommands.CreateNetworkProfile,
            Guid.NewGuid(),
            new { key = "lan", runtimeNetwork = "julos-lan" });

        var first = await worker.InvokeCommandAsync(command, CancellationToken.None);
        Assert.IsTrue(first.Succeeded, first.ErrorCode);

        var second = await worker.InvokeCommandAsync(command, CancellationToken.None);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual("browser.network_exists", second.ErrorCode);
    }

    [TestMethod]
    public async Task CreateProfilePersistsAndListsForOwnerOnly()
    {
        var owner = Guid.NewGuid();
        await SeedNetworkProfileAsync("julos-lan", "julos-lan");
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);

        var created = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.CreateProfile,
                owner,
                new { displayName = "Work", mode = "persistent", networkProfileKey = "julos-lan" }),
            CancellationToken.None);

        Assert.IsTrue(created.Succeeded, created.ErrorCode);
        Assert.AreEqual("persistent", created.Payload.GetProperty("mode").GetString());

        var owned = await worker.InvokeCommandAsync(
            ManageCommand(InteractiveProfilesWorkerCommands.ListProfiles, owner, request: null),
            CancellationToken.None);
        Assert.IsTrue(owned.Succeeded, owned.ErrorCode);
        Assert.AreEqual(1, owned.Payload.GetProperty("profiles").GetArrayLength());

        var other = await worker.InvokeCommandAsync(
            ManageCommand(InteractiveProfilesWorkerCommands.ListProfiles, Guid.NewGuid(), request: null),
            CancellationToken.None);
        Assert.IsTrue(other.Succeeded, other.ErrorCode);
        Assert.AreEqual(0, other.Payload.GetProperty("profiles").GetArrayLength());
    }

    [TestMethod]
    public async Task CreateProfileWithUnknownNetworkFails()
    {
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);

        var result = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.CreateProfile,
                Guid.NewGuid(),
                new { displayName = "Work", mode = "persistent", networkProfileKey = "absent" }),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.network_not_found", result.ErrorCode);
    }

    [TestMethod]
    public async Task CreateProfileRejectsTemporaryMode()
    {
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);

        var result = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.CreateProfile,
                Guid.NewGuid(),
                new { displayName = "Work", mode = "temporary", networkProfileKey = "julos-lan" }),
            CancellationToken.None);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("browser.request_invalid", result.ErrorCode);
    }

    [TestMethod]
    public async Task DeleteProfileRequiresOwnerAndCurrentRevision()
    {
        var owner = Guid.NewGuid();
        await SeedNetworkProfileAsync("julos-lan", "julos-lan");
        var profile = await SeedPersistentProfileAsync(owner, "julos-lan");
        var worker = await StartWorkerAsync(["julos-lan"], defaultNetwork: null, runtimeImage: null);

        var stale = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.DeleteProfile,
                owner,
                new { profileId = profile.ProfileId, revision = profile.Revision + 1 }),
            CancellationToken.None);
        Assert.IsFalse(stale.Succeeded);
        Assert.AreEqual("browser.profile_not_found", stale.ErrorCode);

        var deleted = await worker.InvokeCommandAsync(
            ManageCommand(
                InteractiveProfilesWorkerCommands.DeleteProfile,
                owner,
                new { profileId = profile.ProfileId, revision = profile.Revision }),
            CancellationToken.None);
        Assert.IsTrue(deleted.Succeeded, deleted.ErrorCode);
        Assert.IsTrue(deleted.Payload.GetProperty("deleted").GetBoolean());
    }

    private static PackageWorkerCommand ManageCommand(string name, Guid owner, object? request)
    {
        var payload = new
        {
            OwnerUserId = owner,
            Request = JsonSerializer.SerializeToElement(request ?? new { }, JsonOptions),
        };
        return new PackageWorkerCommand(name, JsonSerializer.SerializeToElement(payload, JsonOptions));
    }

    private static PackageWorkerCommand ResolvePlanCommand(Guid ownerUserId, string initialUrl, string profileMode, Guid? profileId)
    {
        var browserRequest = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["initialUrl"] = initialUrl,
            ["profileMode"] = profileMode,
            ["profileId"] = profileId,
        };
        var payload = new
        {
            OwnerUserId = ownerUserId,
            Request = new
            {
                OperationKey = "test-operation",
                Request = JsonSerializer.SerializeToElement(browserRequest, JsonOptions),
            },
        };
        return new PackageWorkerCommand(
            InteractiveSessionWorkerCommands.ResolvePlan,
            JsonSerializer.SerializeToElement(payload, JsonOptions));
    }

    private static JsonElement EmptyPayload() => JsonSerializer.SerializeToElement(new { }, JsonOptions);
}
