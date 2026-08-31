namespace JulOS.Agent;

/// <summary>Explicit local Docker Engine capability configuration.</summary>
internal sealed record DockerEngineOptions(bool Enabled, string SocketPath, bool ControlEnabled)
{
    internal const string DefaultSocketPath = "/var/run/docker.sock";

    internal static DockerEngineOptions Disabled { get; } = new(false, DefaultSocketPath, false);

    internal static DockerEngineOptions Read(IReadOnlyDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var enabled = ReadBoolean(environment, "JULOS_AGENT_DOCKER_ENABLED");
        var controlEnabled = ReadBoolean(environment, "JULOS_AGENT_DOCKER_CONTROL_ENABLED");
        if (controlEnabled && !enabled)
        {
            throw new InvalidOperationException(
                "JULOS_AGENT_DOCKER_CONTROL_ENABLED requires JULOS_AGENT_DOCKER_ENABLED=true.");
        }

        var socketPath = Optional(environment, "JULOS_AGENT_DOCKER_SOCKET_PATH") ?? DefaultSocketPath;
        if (!Path.IsPathFullyQualified(socketPath))
        {
            throw new InvalidOperationException("JULOS_AGENT_DOCKER_SOCKET_PATH must be an absolute path.");
        }

        return new DockerEngineOptions(enabled, Path.GetFullPath(socketPath), controlEnabled);
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, string?> environment, string name)
    {
        var value = Optional(environment, name);
        if (value is null)
        {
            return false;
        }
        if (!bool.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException($"{name} must be true or false.");
        }
        return parsed;
    }

    private static string? Optional(IReadOnlyDictionary<string, string?> environment, string name)
    {
        if (!environment.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} must not contain surrounding whitespace.");
        }
        return value;
    }
}
