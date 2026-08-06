using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed class AgentEnrollmentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;

    internal AgentEnrollmentClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    internal async Task<AgentProvisioningState> EnrollAsync(
        AgentBootstrapOptions options,
        AgentProvisioningState pending,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.Status != AgentProvisioningStatus.Pending)
        {
            throw new InvalidOperationException("Only pending Agent state can be enrolled.");
        }

        var token = options.EnrollmentToken
            ?? throw new InvalidOperationException(
                "JULOS_AGENT_ENROLLMENT_TOKEN is required until enrollment succeeds.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agent/enroll");
        request.Headers.Add(
            AgentProtocolContract.HeaderName,
            AgentProtocolContract.CurrentVersion.ToString(CultureInfo.InvariantCulture));
        request.Content = JsonContent.Create(
            new RedeemAgentEnrollmentRequest(
                token,
                pending.Credential,
                pending.Name,
                pending.MachineIdentity,
                pending.OperatingSystem,
                pending.Architecture,
                pending.Version),
            options: JsonOptions);
        using var response = await this.httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.UpgradeRequired)
        {
            throw new AgentProtocolException(
                "agent.protocol_incompatible",
                "The Server rejected the Agent protocol version.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Agent enrollment failed with HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        ValidateNegotiatedProtocol(response);
        var enrollment = await response.Content.ReadFromJsonAsync<RedeemAgentEnrollmentResponse>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Agent enrollment response is empty.");
        return pending.Complete(enrollment);
    }

    private static void ValidateNegotiatedProtocol(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(AgentProtocolContract.HeaderName, out var values)
            || !int.TryParse(
                values.SingleOrDefault(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var negotiated)
            || negotiated != AgentProtocolContract.CurrentVersion)
        {
            throw new AgentProtocolException(
                "agent.protocol_negotiation_failed",
                "The Server did not confirm the requested Agent protocol version.");
        }
    }
}

internal sealed class AgentProtocolException : Exception
{
    internal AgentProtocolException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    internal string Code { get; }
}
