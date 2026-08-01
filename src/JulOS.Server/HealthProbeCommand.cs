namespace JulOS.Server;

/// <summary>
/// The container health probe.
/// </summary>
/// <remarks>
/// Starting the application with <c>--health-check</c> requests the readiness endpoint
/// of the local instance and exits with 0 or 1. The runtime image therefore needs no
/// HTTP client tool, which keeps its attack surface limited to the application itself.
/// </remarks>
internal static class HealthProbeCommand
{
    private const string CommandSwitch = "--health-check";

    private const string DefaultAddress = "http://127.0.0.1:8080/health/ready";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Returns whether the process was started as a health probe.</summary>
    internal static bool IsRequested(string[] arguments)
    {
        return Array.IndexOf(arguments, CommandSwitch) >= 0;
    }

    /// <summary>
    /// Requests the readiness endpoint and returns the process exit code.
    /// The argument after <c>--health-check</c> overrides the default address.
    /// </summary>
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var address = ReadAddress(arguments);

        using var client = new HttpClient { Timeout = ProbeTimeout };

        try
        {
            using var response = await client.GetAsync(address).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return 0;
            }

            await Console.Error
                .WriteLineAsync($"{address} answered with status {(int)response.StatusCode}.")
                .ConfigureAwait(false);

            return 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await Console.Error
                .WriteLineAsync($"{address} did not answer within {ProbeTimeout.TotalSeconds:0} seconds: {exception.Message}")
                .ConfigureAwait(false);

            return 1;
        }
    }

    private static Uri ReadAddress(string[] arguments)
    {
        var index = Array.IndexOf(arguments, CommandSwitch);
        var candidate = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : DefaultAddress;

        return Uri.TryCreate(candidate, UriKind.Absolute, out var address)
            ? address
            : new Uri(DefaultAddress);
    }
}
