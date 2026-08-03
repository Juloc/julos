using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using JulOS.Agent;
using JulOS.Contracts.Agents;

namespace JulOS.Agent.Tests;

[TestClass]
public sealed class AgentEnrollmentClientTests
{
    [TestMethod]
    public async Task EnrollmentSendsAndRequiresTheCurrentProtocol()
    {
        var handler = new ProtocolHandler(confirmProtocol: true, confirmedVersion: AgentProtocolContract.CurrentVersion);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost"),
        };
        var pending = PendingState();

        var enrolled = await new AgentEnrollmentClient(httpClient).EnrollAsync(
            Options(),
            pending,
            CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(AgentProvisioningStatus.Enrolled, enrolled.Status);
        Assert.AreEqual(
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture),
            handler.ObservedProtocol);
    }

    [TestMethod]
    public async Task MissingProtocolConfirmationFailsClosed()
    {
        using var httpClient = new HttpClient(new ProtocolHandler(confirmProtocol: false, confirmedVersion: 0))
        {
            BaseAddress = new Uri("https://localhost"),
        };

        var failure = await Assert.ThrowsExactlyAsync<AgentProtocolException>(
            () => new AgentEnrollmentClient(httpClient).EnrollAsync(
                Options(),
                PendingState(),
                CancellationToken.None)).ConfigureAwait(false);

        Assert.AreEqual("agent.protocol_negotiation_failed", failure.Code);
    }

    [TestMethod]
    public async Task DifferentProtocolConfirmationCannotDowngrade()
    {
        using var httpClient = new HttpClient(new ProtocolHandler(
            confirmProtocol: true,
            confirmedVersion: AgentProtocolContract.CurrentVersion + 1))
        {
            BaseAddress = new Uri("https://localhost"),
        };

        var failure = await Assert.ThrowsExactlyAsync<AgentProtocolException>(
            () => new AgentEnrollmentClient(httpClient).EnrollAsync(
                Options(),
                PendingState(),
                CancellationToken.None)).ConfigureAwait(false);

        Assert.AreEqual("agent.protocol_negotiation_failed", failure.Code);
    }

    private static AgentBootstrapOptions Options() => new(
        new Uri("https://localhost"),
        "/tmp/julos-agent-test.json",
        "/tmp/julos-machine-id",
        "enrollment-token-test-value-0000000000000000",
        "test-agent",
        "1.0.0",
        HeartbeatIntervalOverride: null,
        CommandPollIntervalOverride: null);

    private static AgentProvisioningState PendingState() => new(
        AgentProvisioningStatus.Pending,
        AgentId: null,
        Credential: Convert.ToBase64String(new byte[48]).TrimEnd('='),
        EnrolledAtUtc: null,
        HeartbeatIntervalSeconds: null,
        CommandPollIntervalSeconds: null,
        Name: "test-agent",
        MachineIdentity: "machine-identity-test",
        OperatingSystem: "Debian 13",
        Architecture: "x86_64",
        Version: "1.0.0");

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        private readonly bool confirmProtocol;
        private readonly int confirmedVersion;

        internal ProtocolHandler(bool confirmProtocol, int confirmedVersion)
        {
            this.confirmProtocol = confirmProtocol;
            this.confirmedVersion = confirmedVersion;
        }

        internal string? ObservedProtocol { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ObservedProtocol = request.Headers.TryGetValues(
                AgentProtocolContract.HeaderName,
                out var values)
                ? values.SingleOrDefault()
                : null;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new RedeemAgentEnrollmentResponse(
                    Guid.CreateVersion7(),
                    PendingState().Credential,
                    DateTimeOffset.UtcNow,
                    HeartbeatIntervalSeconds: 30,
                    CommandPollIntervalSeconds: 5)),
            };
            if (this.confirmProtocol)
            {
                response.Headers.Add(
                    AgentProtocolContract.HeaderName,
                    this.confirmedVersion.ToString(CultureInfo.InvariantCulture));
            }
            return Task.FromResult(response);
        }
    }
}
