using System.Text.RegularExpressions;

namespace JulOS.Agent;

internal sealed partial record AgentBootstrapOptions(
    Uri ServerEndpoint,
    string IdentityPath,
    string MachineIdentityPath,
    string? EnrollmentToken,
    string Name,
    string Version,
    TimeSpan? HeartbeatIntervalOverride,
    TimeSpan? CommandPollIntervalOverride)
{
    internal static AgentBootstrapOptions Read(IReadOnlyDictionary<string, string?> environment)
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

        var identityPath = ReadAbsolutePath(
            environment,
            "JULOS_AGENT_IDENTITY_PATH",
            DefaultIdentityPath());
        var machineIdentityPath = ReadAbsolutePath(
            environment,
            "JULOS_AGENT_MACHINE_ID_PATH",
            "/etc/machine-id");
        var enrollmentToken = Optional(environment, "JULOS_AGENT_ENROLLMENT_TOKEN");
        if (enrollmentToken is not null
            && (enrollmentToken.Length is < 32 or > 1024 || enrollmentToken.Any(char.IsControl)))
        {
            throw new InvalidOperationException("JULOS_AGENT_ENROLLMENT_TOKEN is invalid.");
        }

        var name = Optional(environment, "JULOS_AGENT_NAME") ?? Environment.MachineName;
        if (!SafeName().IsMatch(name))
        {
            throw new InvalidOperationException("JULOS_AGENT_NAME is invalid.");
        }

        var version = Optional(environment, "JULOS_AGENT_VERSION")
            ?? typeof(AgentBootstrapOptions).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        if (!SemanticVersion().IsMatch(version))
        {
            throw new InvalidOperationException("JULOS_AGENT_VERSION must be a semantic version.");
        }

        return new AgentBootstrapOptions(
            endpoint,
            identityPath,
            machineIdentityPath,
            enrollmentToken,
            name,
            version,
            ReadOptionalInterval(environment, "JULOS_AGENT_HEARTBEAT_SECONDS", 5, 300),
            ReadOptionalInterval(environment, "JULOS_AGENT_COMMAND_POLL_SECONDS", 1, 60));
    }

    private static string DefaultIdentityPath()
    {
        if (OperatingSystem.IsLinux())
        {
            return "/var/lib/julos-agent/identity.json";
        }

        var commonData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(commonData))
        {
            throw new InvalidOperationException("No system application-data directory is available.");
        }

        return Path.Combine(commonData, "JulOS", "Agent", "identity.json");
    }

    private static string ReadAbsolutePath(
        IReadOnlyDictionary<string, string?> environment,
        string name,
        string fallback)
    {
        var value = Optional(environment, name) ?? fallback;
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException($"{name} must be an absolute path.");
        }

        return Path.GetFullPath(value);
    }

    private static string Required(IReadOnlyDictionary<string, string?> environment, string name) =>
        Optional(environment, name)
        ?? throw new InvalidOperationException($"{name} is required.");

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

    private static TimeSpan? ReadOptionalInterval(
        IReadOnlyDictionary<string, string?> environment,
        string name,
        int minimum,
        int maximum)
    {
        var value = Optional(environment, name);
        if (value is null)
        {
            return null;
        }

        if (!int.TryParse(value, out var seconds) || seconds < minimum || seconds > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be an integer from {minimum} through {maximum}.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();
}
