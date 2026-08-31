using System.Text.Json;

using JulOS.AdaptiveBrowser.Worker;
using JulOS.PackageSdk;

namespace JulOS.AdaptiveBrowser.Worker.Tests;

[TestClass]
public sealed class AdaptiveBrowserWorkerTests
{
    private const string RuntimeImage = "ghcr.io/juloc/julos-adaptive-browser-runtime@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ResolvePlanUsesBrowserStreamWithoutVnc()
    {
        var worker = await CreateStartedWorkerAsync().ConfigureAwait(false);
        var request = new CreateInteractiveSessionRequest(
            "test-operation",
            JsonSerializer.SerializeToElement(new
            {
                initialUrl = "https://example.org/",
                executionMode = "server",
                network = "julos-remote",
                viewportWidth = 1440,
                viewportHeight = 900,
                deviceScaleFactor = 1.25m,
            }));
        var command = new PackageWorkerCommand(
            InteractiveSessionWorkerCommands.ResolvePlan,
            JsonSerializer.SerializeToElement(new ResolveInteractiveSessionPlanRequest(Guid.NewGuid(), request)));

        var result = await worker.InvokeCommandAsync(command, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.Succeeded, result.ErrorDetail);
        var plan = result.Payload.Deserialize<InteractiveSessionRuntimePlan>(JsonOptions);
        Assert.IsNotNull(plan);
        Assert.AreEqual("browser-stream", plan.PresentationProtocol);
        Assert.AreEqual(8080, plan.PresentationPort);
        Assert.AreEqual("JULOS_BROWSER_STREAM_TOKEN", plan.Credential.EnvironmentName);
        Assert.AreEqual(RuntimeImage, plan.Image);
        Assert.AreEqual("julos-remote", plan.RuntimeNetwork);
        Assert.AreEqual("https://example.org/", plan.Environment["JULOS_START_URL"]);
        Assert.AreNotEqual("vnc", plan.PresentationProtocol);
    }

    [TestMethod]
    public async Task ResolvePlanRejectsDeviceModeBecauseItNeedsNoServerRuntime()
    {
        var worker = await CreateStartedWorkerAsync().ConfigureAwait(false);
        var request = new CreateInteractiveSessionRequest(
            "test-device",
            JsonSerializer.SerializeToElement(new
            {
                initialUrl = "https://example.org/",
                executionMode = "device",
                viewportWidth = 1280,
                viewportHeight = 800,
                deviceScaleFactor = 1m,
            }));
        var command = new PackageWorkerCommand(
            InteractiveSessionWorkerCommands.ResolvePlan,
            JsonSerializer.SerializeToElement(new ResolveInteractiveSessionPlanRequest(Guid.NewGuid(), request)));

        var result = await worker.InvokeCommandAsync(command, CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("adaptive-browser.execution_mode_invalid", result.ErrorCode);
    }

    [TestMethod]
    public async Task ValidateConfigurationRejectsMutableRuntimeImage()
    {
        var worker = new AdaptiveBrowserWorker(TimeProvider.System);
        var result = await worker.ValidateConfigurationAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtimeImage"] = "ghcr.io/juloc/julos-adaptive-browser-runtime:latest",
                ["allowedNetworks"] = "julos-remote",
                ["defaultNetwork"] = "julos-remote",
            },
            CancellationToken.None).ConfigureAwait(false);

        Assert.IsFalse(result.Valid);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "adaptive-browser.configuration.runtime_image"));
    }

    private static async Task<AdaptiveBrowserWorker> CreateStartedWorkerAsync()
    {
        var worker = new AdaptiveBrowserWorker(TimeProvider.System);
        await worker.ConfigureAsync(
            new PackageWorkerContext(
                "de.juloc.julos.adaptive-browser",
                "0.1.0",
                new Uri("http://127.0.0.1:8080"),
                "test-worker",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["idleTimeoutMinutes"] = "30",
                    ["allowedNetworks"] = "julos-remote",
                    ["defaultNetwork"] = "julos-remote",
                    ["runtimeImage"] = RuntimeImage,
                },
                ["interactive.session"]),
            CancellationToken.None).ConfigureAwait(false);
        await worker.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return worker;
    }
}
