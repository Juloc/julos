using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Agents;

using Microsoft.AspNetCore.Mvc.Testing;

namespace JulOS.Integration.Tests.Agents;

[TestClass]
public sealed class AgentProtocolTests
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    };

    [TestMethod]
    public async Task MissingProtocolFailsClosedWithSupportedRange()
    {
        using var host = new ServerHost();
        using var client = host.CreateClient(ClientOptions);
        client.DefaultRequestHeaders.Remove(AgentProtocolContract.HeaderName);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            new { }).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.AreEqual(
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            response.Headers.GetValues(AgentProtocolContract.HeaderName).Single());
        Assert.AreEqual(
            AgentProtocolContract.MinimumSupportedVersion.ToString(CultureInfo.InvariantCulture),
            response.Headers.GetValues(AgentProtocolContract.MinimumHeaderName).Single());
        Assert.AreEqual(
            AgentProtocolContract.MaximumSupportedVersion.ToString(CultureInfo.InvariantCulture),
            response.Headers.GetValues(AgentProtocolContract.MaximumHeaderName).Single());
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        Assert.AreEqual(
            "agent.protocol_incompatible",
            document.RootElement.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task UnsupportedProtocolCannotSilentlyDowngrade()
    {
        using var host = new ServerHost();
        using var client = host.CreateClient(ClientOptions);
        client.DefaultRequestHeaders.Remove(AgentProtocolContract.HeaderName);
        client.DefaultRequestHeaders.Add(
            AgentProtocolContract.HeaderName,
            (AgentProtocolContract.MaximumSupportedVersion + 1).ToString(CultureInfo.InvariantCulture));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            new { }).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.AreEqual(
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            response.Headers.GetValues(AgentProtocolContract.HeaderName).Single());
    }

    [TestMethod]
    public async Task CurrentProtocolReachesTheRuntimeEndpoint()
    {
        using var host = new ServerHost();
        using var client = host.CreateClient(ClientOptions);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            new { }).ConfigureAwait(false);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            response.Headers.GetValues(AgentProtocolContract.HeaderName).Single());
    }
}
