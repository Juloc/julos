using System.Text.Json;

using JulOS.Infrastructure.Packages;
using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
public sealed class InteractiveProfilesCapabilityProviderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CallerPackageId = "de.juloc.julos.browser";

    [TestMethod]
    public async Task CreateForwardsOwnerScopedEnvelopeToCallerWorker()
    {
        var dispatcher = new RecordingDispatcher(
            new PackageWorkerCommandResult(true, null, null, Element(new { profileId = "p" })));
        var provider = new InteractiveProfilesCapabilityProvider(dispatcher);
        var userId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var payload = Element(new { displayName = "Work", mode = "persistent", networkProfileKey = "lan" });

        var response = await provider
            .InvokeAsync(Request(InteractiveProfilesCapabilityContract.CreateOperation, payload, userId))
            .ConfigureAwait(false);

        Assert.IsTrue(response.Succeeded, response.ErrorCode);
        Assert.AreEqual(CallerPackageId, dispatcher.Package);
        Assert.AreEqual(InteractiveProfilesWorkerCommands.CreateProfile, dispatcher.Command?.Name);

        var envelope = dispatcher.Command!.Payload.Deserialize<ManageInteractiveProfilesRequest>(JsonOptions);
        Assert.IsNotNull(envelope);
        Assert.AreEqual(userId, envelope!.OwnerUserId);
        Assert.AreEqual("Work", envelope.Request.GetProperty("displayName").GetString());
        Assert.AreEqual("p", response.Payload.GetProperty("profileId").GetString());
    }

    [TestMethod]
    public async Task ListNetworksMapsToTheListNetworkProfilesCommand()
    {
        var dispatcher = new RecordingDispatcher(Ok());
        var provider = new InteractiveProfilesCapabilityProvider(dispatcher);

        var response = await provider
            .InvokeAsync(Request(
                InteractiveProfilesCapabilityContract.ListNetworksOperation,
                Element(new { }),
                Guid.NewGuid()))
            .ConfigureAwait(false);

        Assert.IsTrue(response.Succeeded, response.ErrorCode);
        Assert.AreEqual(InteractiveProfilesWorkerCommands.ListNetworkProfiles, dispatcher.Command?.Name);
    }

    [TestMethod]
    public async Task WorkerFailureIsPropagatedAsCallerSafeFailure()
    {
        var dispatcher = new RecordingDispatcher(
            new PackageWorkerCommandResult(false, "browser.network_denied", "denied", Element(new { })));
        var provider = new InteractiveProfilesCapabilityProvider(dispatcher);

        var response = await provider
            .InvokeAsync(Request(
                InteractiveProfilesCapabilityContract.CreateNetworkOperation,
                Element(new { key = "x", runtimeNetwork = "guest" }),
                Guid.NewGuid()))
            .ConfigureAwait(false);

        Assert.IsFalse(response.Succeeded);
        Assert.AreEqual("browser.network_denied", response.ErrorCode);
    }

    [TestMethod]
    public async Task IncompatibleContractVersionIsRejectedWithoutDispatch()
    {
        var dispatcher = new RecordingDispatcher(Ok());
        var provider = new InteractiveProfilesCapabilityProvider(dispatcher);
        var request = new CapabilityRequest(
            InteractiveProfilesCapabilityContract.Name,
            "9.9.9",
            InteractiveProfilesCapabilityContract.CreateOperation,
            "corr",
            Element(new { }),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new CapabilityCallerContext(CallerPackageId, Guid.NewGuid()));

        var response = await provider.InvokeAsync(request).ConfigureAwait(false);

        Assert.IsFalse(response.Succeeded);
        Assert.AreEqual("interactive.profiles.contract_incompatible", response.ErrorCode);
        Assert.IsNull(dispatcher.Package);
    }

    [TestMethod]
    public async Task MissingAuthenticatedCallerIsRejectedWithoutDispatch()
    {
        var dispatcher = new RecordingDispatcher(Ok());
        var provider = new InteractiveProfilesCapabilityProvider(dispatcher);

        var response = await provider
            .InvokeAsync(Request(
                InteractiveProfilesCapabilityContract.ListOperation,
                Element(new { }),
                userId: null))
            .ConfigureAwait(false);

        Assert.IsFalse(response.Succeeded);
        Assert.AreEqual("interactive.profiles.caller_invalid", response.ErrorCode);
        Assert.IsNull(dispatcher.Package);
    }

    [TestMethod]
    public async Task UnsupportedOperationIsRejectedWithoutDispatch()
    {
        var dispatcher = new RecordingDispatcher(Ok());
        var provider = new InteractiveProfilesCapabilityProvider(dispatcher);

        var response = await provider
            .InvokeAsync(Request("bogus", Element(new { }), Guid.NewGuid()))
            .ConfigureAwait(false);

        Assert.IsFalse(response.Succeeded);
        Assert.AreEqual("interactive.profiles.operation_unsupported", response.ErrorCode);
        Assert.IsNull(dispatcher.Package);
    }

    private static PackageWorkerCommandResult Ok() =>
        new(true, null, null, JsonSerializer.SerializeToElement(new { }, JsonOptions));

    private static JsonElement Element(object value) =>
        JsonSerializer.SerializeToElement(value, JsonOptions);

    private static CapabilityRequest Request(string operation, JsonElement payload, Guid? userId) =>
        new(
            InteractiveProfilesCapabilityContract.Name,
            InteractiveProfilesCapabilityContract.Version,
            operation,
            "corr",
            payload,
            DateTimeOffset.UtcNow.AddMinutes(1),
            userId is null ? null : new CapabilityCallerContext(CallerPackageId, userId));

    private sealed class RecordingDispatcher : IPackageWorkerCommandDispatcher
    {
        private readonly PackageWorkerCommandResult result;

        public RecordingDispatcher(PackageWorkerCommandResult result) => this.result = result;

        public string? Package { get; private set; }

        public PackageWorkerCommand? Command { get; private set; }

        public Task<PackageWorkerCommandResult> InvokeAsync(
            string packageId,
            PackageWorkerCommand command,
            CancellationToken cancellationToken)
        {
            this.Package = packageId;
            this.Command = command;
            return Task.FromResult(this.result);
        }
    }
}
