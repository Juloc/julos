using System.Text.RegularExpressions;

namespace JulOS.RuntimeManager;

/// <summary>Validates every package runtime request against immutable isolation policy.</summary>
public sealed class RuntimePolicy
{
    private static readonly Regex IdentifierPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex RuntimeIdentifierPattern = new(
        "^[a-z0-9][a-z0-9-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex VolumeNamePattern = new(
        "^[a-z0-9][a-z0-9_.-]{0,254}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex ImageDigestPattern = new(
        "^[^@\\s]+@sha256:[a-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private readonly HashSet<string> allowedNetworks;

    /// <summary>Creates a policy with the exact Docker networks packages may use.</summary>
    /// <param name="allowedNetworks">Allowlisted non-host networks.</param>
    public RuntimePolicy(IEnumerable<string> allowedNetworks)
    {
        ArgumentNullException.ThrowIfNull(allowedNetworks);
        var networks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var network in allowedNetworks)
        {
            if (!RuntimeIdentifierPattern.IsMatch(network) || network is "host" or "none")
            {
                throw new ArgumentException($"Runtime network '{network}' is invalid.", nameof(allowedNetworks));
            }

            networks.Add(network);
        }

        this.allowedNetworks = networks;
    }

    /// <summary>Rejects unpinned images, unapproved networks, foreign volumes and secret-like environment fields.</summary>
    /// <param name="request">Runtime request to validate.</param>
    public void Validate(RuntimeCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!RuntimeIdentifierPattern.IsMatch(request.RuntimeId))
        {
            throw Failure("runtime.id.invalid", "The runtime identifier is invalid.");
        }

        if (!IdentifierPattern.IsMatch(request.PackageId))
        {
            throw Failure("runtime.package.invalid", "The package identifier is invalid.");
        }

        if (!ImageDigestPattern.IsMatch(request.Image))
        {
            throw Failure(
                "runtime.image.unpinned",
                "Runtime images must use an immutable sha256 digest.");
        }

        if (request.CpuLimit <= 0 || request.CpuLimit > 64)
        {
            throw Failure("runtime.cpu.invalid", "CPU limit must be greater than zero and at most 64.");
        }

        if (request.MemoryLimitMb < 16 || request.MemoryLimitMb > 262144)
        {
            throw Failure(
                "runtime.memory.invalid",
                "Memory limit must be between 16 and 262144 MiB.");
        }

        foreach (var network in request.Networks)
        {
            if (!this.allowedNetworks.Contains(network))
            {
                throw Failure(
                    "runtime.network.denied",
                    $"Runtime network '{network}' is not allowlisted.");
            }
        }

        var volumeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var volume in request.Volumes)
        {
            var requiredPrefix = $"julos-{request.PackageId.Replace('.', '-')}-";
            if (!VolumeNamePattern.IsMatch(volume.Name)
                || !volume.Name.StartsWith(requiredPrefix, StringComparison.Ordinal)
                || !volumeNames.Add(volume.Name))
            {
                throw Failure(
                    "runtime.volume.denied",
                    "Only unique package-owned named volumes are allowed.");
            }

            if (!IsAbsoluteContainerPath(volume.Target))
            {
                throw Failure(
                    "runtime.volume.target_invalid",
                    "Runtime volume targets must be absolute container paths without traversal.");
            }
        }

        foreach (var pair in request.Environment)
        {
            if (!IsEnvironmentName(pair.Key)
                || pair.Value.Contains('\0')
                || LooksLikeSecretName(pair.Key))
            {
                throw Failure(
                    "runtime.environment.invalid",
                    "Runtime environment entries contain an invalid name, value or secret-like field.");
            }
        }
    }

    /// <summary>Returns the Docker ownership label for a package.</summary>
    public static string OwnershipLabel(string packageId) =>
        $"com.juloc.julos.package={packageId}";

    /// <summary>Returns the Docker identity label for a managed runtime.</summary>
    public static string RuntimeLabel(string runtimeId) =>
        $"com.juloc.julos.runtime={runtimeId}";

    private static bool IsAbsoluteContainerPath(string value)
    {
        return value.StartsWith('/')
            && !value.Contains("..", StringComparison.Ordinal)
            && !value.Contains('\\')
            && value.Length <= 512;
    }

    private static bool IsEnvironmentName(string value)
    {
        return value.Length is > 0 and <= 128
            && (char.IsLetter(value[0]) || value[0] == '_')
            && value.All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static bool LooksLikeSecretName(string value)
    {
        return value.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SECRET", StringComparison.OrdinalIgnoreCase)
            || value.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)
            || value.Contains("PRIVATE_KEY", StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeManagerException Failure(string code, string message) => new(code, message);
}
