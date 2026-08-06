using System.Net;
using System.Text.Json;

using JulOS.Contracts.Errors;

namespace JulOS.Integration.Tests.Concurrency;

/// <summary>Verifies the public HTTP representation of an optimistic-concurrency conflict.</summary>
[TestClass]
public sealed class ConcurrencyProblemDetailsTests
{
    [TestMethod]
    public async Task AConflictReturnsHttp409AndTheCurrentRevision()
    {
        using var host = new ServerHost(includeConcurrencyConflictEndpoint: true);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/__tests/concurrency-conflict", UriKind.Relative));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync());

        Assert.AreEqual(
            PlatformErrorCodes.ConcurrencyConflict,
            body.GetProperty(ProblemExtensionNames.Code).GetString());
        Assert.AreEqual(7, body.GetProperty(ProblemExtensionNames.CurrentRevision).GetInt32());
        Assert.IsFalse(body.GetProperty(ProblemExtensionNames.Retryable).GetBoolean());
    }
}
