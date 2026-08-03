using System.Net.Http.Headers;

namespace JulOS.Agent;

/// <summary>Entry point of the JulOS Agent process.</summary>
internal static class Program
{
    private static readonly string[] EnvironmentNames =
    [
        "JULOS_SERVER_URL",
        "JULOS_AGENT_ID",
        "JULOS_AGENT_CREDENTIAL",
        "JULOS_AGENT_VERSION",
        "JULOS_AGENT_HEARTBEAT_SECONDS",
        "JULOS_AGENT_COMMAND_POLL_SECONDS",
    ];

    private static async Task<int> Main()
    {
        AgentOptions options;
        try
        {
            options = AgentOptions.Read(EnvironmentNames.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal));
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"JulOS Agent configuration error: {exception.Message}");
            return 2;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArguments) =>
        {
            eventArguments.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            return await RunAsync(options, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"JulOS Agent stopped because of an unexpected {exception.GetType().Name}.");
            return 1;
        }
    }

    private static async Task<int> RunAsync(AgentOptions options, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = options.ServerEndpoint,
            Timeout = TimeSpan.FromSeconds(30),
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JulOS-Agent", options.Version));

        var timeProvider = TimeProvider.System;
        var client = new AgentClient(
            httpClient,
            options,
            new LinuxMetricsCollector(timeProvider),
            new AgentCommandExecutor(timeProvider, options.Version),
            timeProvider);
        var reconnectDelay = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await client.RunAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            }
            catch (HttpRequestException exception)
            {
                var status = exception.StatusCode is null
                    ? "transport unavailable"
                    : $"HTTP {(int)exception.StatusCode.Value}";
                Console.Error.WriteLine(
                    $"JulOS Agent connection failed ({status}); retrying in {reconnectDelay.TotalSeconds:0} seconds.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    $"JulOS Agent request timed out; retrying in {reconnectDelay.TotalSeconds:0} seconds.");
            }

            await Task.Delay(reconnectDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            reconnectDelay = TimeSpan.FromSeconds(Math.Min(30, reconnectDelay.TotalSeconds * 2));
        }

        return 0;
    }
}
