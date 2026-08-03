using System.Text.Json;

using JulOS.PackageSdk;

namespace JulOS.Infrastructure.Tests.Packages;

[TestClass]
[DoNotParallelize]
public sealed class PackageWorkerHostTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task StandardIoHostDispatchesValidationAndControlledShutdown()
    {
        var originalInput = Console.In;
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var requests = new[]
        {
            JsonSerializer.Serialize(new PackageWorkerProtocolRequest(
                "validate-1",
                "validate",
                JsonSerializer.SerializeToElement<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["mode"] = "safe" },
                    JsonOptions),
                1000), JsonOptions),
            JsonSerializer.Serialize(new PackageWorkerProtocolRequest(
                "shutdown-1",
                "shutdown",
                JsonSerializer.SerializeToElement(new { }, JsonOptions),
                1000), JsonOptions),
        };
        Console.SetIn(new StringReader(string.Join(Environment.NewLine, requests) + Environment.NewLine));
        Console.SetOut(output);
        Console.SetError(error);
        var worker = new RecordingWorker();

        try
        {
            var exitCode = await PackageWorkerHost.RunAsync(
                worker,
                [PackageWorkerHost.StandardIoSwitch]).ConfigureAwait(false);

            Assert.AreEqual(0, exitCode);
            Assert.AreEqual("safe", worker.ValidatedConfiguration["mode"]);
            Assert.IsTrue(worker.StopCalled);
            var responses = output.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonSerializer.Deserialize<PackageWorkerProtocolResponse>(line, JsonOptions))
                .ToArray();
            Assert.AreEqual(2, responses.Length);
            Assert.IsTrue(responses.All(response => response is { Succeeded: true }));
            Assert.AreEqual("validate-1", responses[0]!.Id);
            Assert.AreEqual("shutdown-1", responses[1]!.Id);
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    [TestMethod]
    public async Task HostRefusesDirectExecutionWithoutTransportSwitch()
    {
        var originalError = Console.Error;
        using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Console.SetError(error);
        try
        {
            var exitCode = await PackageWorkerHost.RunAsync(new RecordingWorker(), []).ConfigureAwait(false);
            Assert.AreEqual(2, exitCode);
            StringAssert.Contains(error.ToString(), PackageWorkerHost.StandardIoSwitch);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    private sealed class RecordingWorker : IJulOsPackageWorker
    {
        internal IReadOnlyDictionary<string, string> ValidatedConfiguration { get; private set; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal bool StopCalled { get; private set; }

        public Task<PackageValidationResult> ValidateConfigurationAsync(
            IReadOnlyDictionary<string, string> configuration,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ValidatedConfiguration = configuration;
            return Task.FromResult(new PackageValidationResult(true, []));
        }

        public Task ConfigureAsync(PackageWorkerContext context, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PackageRegistration> RegisterAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PackageRegistration([], [], [], []));

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.StopCalled = true;
            return Task.CompletedTask;
        }

        public Task<PackageHealthSnapshot> ReadHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PackageHealthSnapshot(
                "healthy",
                DateTimeOffset.UnixEpoch,
                null,
                new Dictionary<string, decimal?>(StringComparer.Ordinal)));
    }
}
