using System.Net;
using System.Text.Json;

using JulOS.Contracts.Errors;

namespace JulOS.Integration.Tests.Errors;

/// <summary>Verifies that every failing response uses the JulOS problem shape.</summary>
[TestClass]
public sealed class ProblemDetailsTests
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private static ServerHost host = null!;

    [ClassInitialize]
    public static void StartHost(TestContext context)
    {
        host = new ServerHost();
    }

    [ClassCleanup]
    public static void StopHost()
    {
        host.Dispose();
    }

    [TestMethod]
    public async Task AnUnknownRouteReturnsTheJulosProblemShape()
    {
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadProblemAsync(response);

        Assert.AreEqual(PlatformErrorCodes.NotFound, problem.GetProperty(ProblemExtensionNames.Code).GetString());
        Assert.IsFalse(problem.GetProperty(ProblemExtensionNames.Retryable).GetBoolean());
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(problem.GetProperty(ProblemExtensionNames.CorrelationId).GetString()),
            "A failure without a correlation identifier cannot be traced to its log entries.");
    }

    [TestMethod]
    public async Task TheCorrelationIdInTheBodyMatchesTheResponseHeader()
    {
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative));

        var problem = await ReadProblemAsync(response);
        var fromBody = problem.GetProperty(ProblemExtensionNames.CorrelationId).GetString();
        var fromHeader = response.Headers.GetValues(CorrelationIdHeader).Single();

        Assert.AreEqual(fromHeader, fromBody);
    }

    [TestMethod]
    public async Task ASafeSuppliedCorrelationIdIsEchoed()
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/does-not-exist", UriKind.Relative));
        request.Headers.Add(CorrelationIdHeader, "caller-supplied-1234");

        using var response = await client.SendAsync(request);

        Assert.AreEqual("caller-supplied-1234", response.Headers.GetValues(CorrelationIdHeader).Single());
    }

    [TestMethod]
    public async Task AnUnsafeSuppliedCorrelationIdIsReplaced()
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/does-not-exist", UriKind.Relative));

        // A value with reserved characters could carry a forged entry into a log file.
        request.Headers.TryAddWithoutValidation(CorrelationIdHeader, "spoofed value; level=Error");

        using var response = await client.SendAsync(request);

        var returned = response.Headers.GetValues(CorrelationIdHeader).Single();

        Assert.AreNotEqual("spoofed value; level=Error", returned);
        Assert.IsTrue(Guid.TryParse(returned, out _), "The replacement must be a generated identifier.");
    }

    [TestMethod]
    public async Task AFailureBodyExposesNoStackTraceOrInternalPath()
    {
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/does-not-exist", UriKind.Relative));

        var body = await response.Content.ReadAsStringAsync();

        Assert.IsFalse(body.Contains("at JulOS", StringComparison.Ordinal), "A stack frame must never reach the client.");
        Assert.IsFalse(body.Contains(".cs:line", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ASuccessfulResponseAlsoCarriesACorrelationId()
    {
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/system/version", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        Assert.IsTrue(
            response.Headers.Contains(CorrelationIdHeader),
            "Support requests quote the identifier of a request that succeeded but behaved unexpectedly.");
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<JsonElement>(body);
    }
}
