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
        using var response = await this.httpClient.PostAsJsonAsync(
            "/api/v1/agent/enroll",
            new RedeemAgentEnrollmentRequest(
                token,
                pending.Credential,
                pending.Name,
                pending.MachineIdentity,
                pending.OperatingSystem,
                pending.Architecture,
                pending.Version),
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Agent enrollment failed with HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        var enrollment = await response.Content.ReadFromJsonAsync<RedeemAgentEnrollmentResponse>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Agent enrollment response is empty.");
        return pending.Complete(enrollment);
    }
}
