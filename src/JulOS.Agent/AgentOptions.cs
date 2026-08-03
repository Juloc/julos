namespace JulOS.Agent;

internal sealed record AgentOptions(
    Uri ServerEndpoint,
    Guid AgentId,
    string Credential,
    string Version,
    TimeSpan HeartbeatInterval,
    TimeSpan CommandPollInterval)
{
    internal static AgentOptions Read(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var endpointText = Required(environment, "JULOS_SERVER_URL");
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http")
            || (endpoint.Scheme == "http" && !endpoint.IsLoopback))
        {
            throw new InvalidOperationException(
                "JULOS_SERVER_URL must be HTTPS, except loopback HTTP used for local development.");
        }

        if (!Guid.TryParseExact(Required(environment, "JULOS_AGENT_ID"), "D", out var agentId))
        {
            throw new InvalidOperationException("JULOS_AGENT_ID must be a canonical GUID.");
        }

        var credential = Required(environment, "JULOS_AGENT_CREDENTIAL");
        if (credential.Length is < 32 or > 1024 || credential.Any(char.IsControl))
        {
            throw new InvalidOperationException("JULOS_AGENT_CREDENTIAL is invalid.");
        }

        var version = environment.TryGetValue("JULOS_AGENT_VERSION", out var configuredVersion)
            && !string.IsNullOrWhiteSpace(configuredVersion)
            ? configuredVersion.Trim()
            : typeof(AgentOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return new AgentOptions(
            endpoint,
            agentId,
            credential,
            version,
            ReadInterval(environment, "JULOS_AGENT_HEARTBEAT_SECONDS", 30, 5, 300),
            ReadInterval(environment, "JULOS_AGENT_COMMAND_POLL_SECONDS", 5, 1, 60));
    }

    private static string Required(IReadOnlyDictionary<string, string?> environment, string name)
    {
        return environment.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && value == value.Trim()
            ? value
            : throw new InvalidOperationException($"{name} is required.");
    }

    private static TimeSpan ReadInterval(
        IReadOnlyDictionary<string, string?> environment,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        var seconds = fallback;
        if (environment.TryGetValue(name, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && (!int.TryParse(value, out seconds) || seconds < minimum || seconds > maximum))
        {
            throw new InvalidOperationException(
                $"{name} must be an integer from {minimum} through {maximum}.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
