using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

using JulOS.Contracts.Agents;

namespace JulOS.Agent;

internal sealed class AgentClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SupportedCommands =
    [
        "diagnostics.snapshot",
    ];

    private readonly HttpClient httpClient;
    private readonly AgentOptions options;
    private readonly LinuxMetricsCollector metricsCollector;
    private readonly AgentCommandExecutor commandExecutor;
    private readonly TimeProvider timeProvider;

    internal AgentClient(
        HttpClient httpClient,
        AgentOptions options,
        LinuxMetricsCollector metricsCollector,
        AgentCommandExecutor commandExecutor,
        TimeProvider timeProvider)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        this.commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var heartbeatDue = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = this.timeProvider.GetUtcNow();
            if (now >= heartbeatDue)
            {
                await this.SendHeartbeatAndMetricsAsync(cancellationToken).ConfigureAwait(false);
                heartbeatDue = now + this.options.HeartbeatInterval;
            }

            await this.PollCommandAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(this.options.CommandPollInterval, this.timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SendHeartbeatAndMetricsAsync(CancellationToken cancellationToken)
    {
        var heartbeat = new AgentHeartbeatRequest(
            this.options.Version,
            [
                new AgentCapabilityContract(
                    "host.metrics.linux",
                    1,
                    OperatingSystem.IsLinux(),
                    1,
                    JsonSerializer.SerializeToElement(new
                    {
                        operatingSystem = RuntimeInformation.OSDescription,
                        architecture = RuntimeInformation.OSArchitecture.ToString(),
                    })),
                new AgentCapabilityContract(
                    "agent.commands.core",
                    1,
                    true,
                    1,
                    JsonSerializer.SerializeToElement(new
                    {
                        commands = SupportedCommands,
                    })),
            ],
            this.timeProvider.GetUtcNow());
        await this.SendAsync(
            HttpMethod.Post,
            "/api/v1/agent/heartbeat",
            heartbeat,
            cancellationToken).ConfigureAwait(false);

        var metrics = await this.metricsCollector.CollectAsync(cancellationToken).ConfigureAwait(false);
        await this.SendAsync(
            HttpMethod.Post,
            "/api/v1/agent/metrics",
            new AgentMetricBatchRequest(metrics),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PollCommandAsync(CancellationToken cancellationToken)
    {
        using var request = this.CreateRequest(HttpMethod.Get, "/api/v1/agent/commands/next");
        using var response = await this.httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var command = await response.Content.ReadFromJsonAsync<AgentCommandResponse>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Agent command response is empty.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remaining = command.ExpiresAtUtc - this.timeProvider.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            deadline.CancelAfter(remaining);
        }
        else
        {
            deadline.Cancel();
        }

        AgentCommandExecution execution;
        try
        {
            execution = await this.commandExecutor.ExecuteAsync(command, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            execution = new AgentCommandExecution(
                false,
                JsonSerializer.SerializeToElement(new { }),
                "agent.command_deadline_exceeded");
        }

        await this.SendAsync(
            HttpMethod.Post,
            $"/api/v1/agent/commands/{command.CommandId:D}/complete",
            new CompleteAgentCommandRequest(
                execution.Succeeded,
                execution.Result,
                execution.ErrorCode,
                command.Revision),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync<T>(
        HttpMethod method,
        string path,
        T body,
        CancellationToken cancellationToken)
    {
        using var request = this.CreateRequest(method, path);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await this.httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-JulOS-Agent-Id", this.options.AgentId.ToString("D"));
        request.Headers.Add("X-JulOS-Agent-Credential", this.options.Credential);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (detail.Length > 512)
        {
            detail = detail[..512];
        }

        throw new HttpRequestException(
            $"JulOS Agent request failed with status {(int)response.StatusCode}: {detail}",
            inner: null,
            response.StatusCode);
    }
}
