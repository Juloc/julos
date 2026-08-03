using System.Net.Http.Headers;

namespace JulOS.Agent;

/// <summary>Entry point of the JulOS Agent process.</summary>
internal static class Program
{
    private static readonly string[] EnvironmentNames =
    [
        "JULOS_SERVER_URL",
        "JULOS_AGENT_IDENTITY_PATH",
        "JULOS_AGENT_MACHINE_ID_PATH",
        "JULOS_AGENT_ENROLLMENT_TOKEN",
        "JULOS_AGENT_NAME",
        "JULOS_AGENT_VERSION",
        "JULOS_AGENT_HEARTBEAT_SECONDS",
        "JULOS_AGENT_COMMAND_POLL_SECONDS",
    ];

    private static async Task<int> Main()
    {
        AgentBootstrapOptions bootstrap;
        try
        {
            bootstrap = AgentBootstrapOptions.Read(EnvironmentNames.ToDictionary(
                name => name,
                Environment.GetEnvironmentVariable,
                StringComparer.Ordinal));
            Environment.SetEnvironmentVariable("JULOS_AGENT_ENROLLMENT_TOKEN", null);
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
            return await RunAsync(bootstrap, shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return 0;
        }
        catch (AgentProtocolException exception)
        {
            Console.Error.WriteLine($"JulOS Agent protocol error ({exception.Code}): {exception.Message}");
            return 5;
        }
        catch (HttpRequestException exception)
        {
            var status = exception.StatusCode is null
                ? "transport unavailable"
                : $"HTTP {(int)exception.StatusCode.Value}";
            Console.Error.WriteLine($"JulOS Agent enrollment failed ({status}).");
            return 3;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"JulOS Agent identity error: {exception.Message}");
            return 4;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("JulOS Agent identity state is not accessible.");
            return 4;
        }
        catch (IOException)
        {
            Console.Error.WriteLine("JulOS Agent identity state could not be read or written.");
            return 4;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"JulOS Agent stopped because of an unexpected {exception.GetType().Name}.");
            return 1;
        }
    }

    private static async Task<int> RunAsync(
        AgentBootstrapOptions bootstrap,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = bootstrap.ServerEndpoint,
            Timeout = TimeSpan.FromSeconds(30),
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("JulOS-Agent", bootstrap.Version));

        var timeProvider = TimeProvider.System;
        var diagnostics = new AgentRuntimeDiagnostics(timeProvider.GetUtcNow());
        var capabilityInventory = new AgentCapabilityInventory();
        var identityStore = new AgentIdentityStore(bootstrap.IdentityPath);
        var provisioner = new AgentProvisioner(
            identityStore,
            new AgentEnrollmentClient(httpClient),
            timeProvider);
        var identity = await provisioner.ResolveAsync(bootstrap, cancellationToken).ConfigureAwait(false);
        var options = AgentOptions.Create(bootstrap, identity);
        var commandExecutor = new AgentCommandExecutor(
            timeProvider,
            options.Version,
            capabilityInventory,
            diagnostics);
        var client = new AgentClient(
            httpClient,
            options,
            new LinuxMetricsCollector(timeProvider),
            commandExecutor,
            timeProvider,
            capabilityInventory,
            diagnostics);
        var reconnectDelay = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            diagnostics.RecordConnectionAttempt();
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
                var failureKind = exception.StatusCode is null
                    ? "transport"
                    : $"http-{(int)exception.StatusCode.Value}";
                diagnostics.RecordConnectionFailure(
                    timeProvider.GetUtcNow(),
                    failureKind,
                    reconnectDelay);
                Console.Error.WriteLine(
                    $"JulOS Agent connection failed ({status}); retrying in {reconnectDelay.TotalSeconds:0} seconds.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                diagnostics.RecordConnectionFailure(
                    timeProvider.GetUtcNow(),
                    "timeout",
                    reconnectDelay);
                Console.Error.WriteLine(
                    $"JulOS Agent request timed out; retrying in {reconnectDelay.TotalSeconds:0} seconds.");
            }

            await Task.Delay(reconnectDelay, timeProvider, cancellationToken).ConfigureAwait(false);
            reconnectDelay = TimeSpan.FromSeconds(Math.Min(30, reconnectDelay.TotalSeconds * 2));
        }

        return 0;
    }
}
