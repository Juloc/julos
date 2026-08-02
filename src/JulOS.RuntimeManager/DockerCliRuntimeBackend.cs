using System.Diagnostics;
using System.Globalization;

namespace JulOS.RuntimeManager;

/// <summary>
/// Owns only containers carrying the JulOS managed and runtime labels. All Docker
/// arguments are added without a shell and every create request passes <see cref="RuntimePolicy"/>.
/// </summary>
public sealed class DockerCliRuntimeBackend : IRuntimeBackend
{
    private const string ManagedLabel = "com.juloc.julos.managed=true";
    private const int MaximumOutputLength = 65536;
    private readonly RuntimePolicy policy;
    private readonly string dockerExecutable;

    /// <summary>Creates a Docker CLI backend with the mandatory isolation policy.</summary>
    /// <param name="policy">Runtime validation policy.</param>
    /// <param name="dockerExecutable">Docker CLI executable.</param>
    public DockerCliRuntimeBackend(RuntimePolicy policy, string dockerExecutable = "docker")
    {
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        if (string.IsNullOrWhiteSpace(dockerExecutable))
        {
            throw new ArgumentException("Docker executable is required.", nameof(dockerExecutable));
        }

        this.dockerExecutable = dockerExecutable;
    }

    /// <inheritdoc />
    public async Task<RuntimeResource> CreateAsync(
        RuntimeCreateRequest request,
        CancellationToken cancellationToken)
    {
        this.policy.Validate(request);
        if (await this.ReadAsync(request.RuntimeId, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw Failure("runtime.already_exists", "A runtime with that identifier already exists.");
        }

        var arguments = new List<string>
        {
            "container",
            "create",
            "--name",
            $"julos-{request.RuntimeId}",
            "--label",
            ManagedLabel,
            "--label",
            RuntimePolicy.OwnershipLabel(request.PackageId),
            "--label",
            RuntimePolicy.RuntimeLabel(request.RuntimeId),
            "--cpus",
            request.CpuLimit.ToString(CultureInfo.InvariantCulture),
            "--memory",
            $"{request.MemoryLimitMb.ToString(CultureInfo.InvariantCulture)}m",
            "--security-opt",
            "no-new-privileges=true",
            "--cap-drop",
            "ALL",
        };

        foreach (var network in request.Networks)
        {
            arguments.Add("--network");
            arguments.Add(network);
        }

        foreach (var volume in request.Volumes)
        {
            arguments.Add("--mount");
            arguments.Add(
                $"type=volume,src={volume.Name},dst={volume.Target}"
                + (volume.ReadOnly ? ",readonly" : string.Empty));
        }

        foreach (var pair in request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            arguments.Add("--env");
            arguments.Add($"{pair.Key}={pair.Value}");
        }

        arguments.Add(request.Image);
        var containerId = (await this.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false)).Trim();
        if (containerId.Length == 0)
        {
            throw Failure("runtime.create.failed", "Docker did not return a container identifier.");
        }

        return new RuntimeResource(
            request.RuntimeId,
            request.PackageId,
            containerId,
            "created",
            request.Image);
    }

    /// <inheritdoc />
    public async Task<RuntimeResource?> ReadAsync(string runtimeId, CancellationToken cancellationToken)
    {
        ValidateRuntimeId(runtimeId);
        var output = await this.ExecuteAsync(
            [
                "container",
                "ls",
                "--all",
                "--filter",
                $"label={ManagedLabel}",
                "--filter",
                $"label={RuntimePolicy.RuntimeLabel(runtimeId)}",
                "--format",
                "{{.ID}}\t{{.State}}\t{{.Image}}\t{{.Label \"com.juloc.julos.package\"}}",
            ],
            cancellationToken).ConfigureAwait(false);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        if (lines.Length != 1)
        {
            throw Failure(
                "runtime.identity.ambiguous",
                "More than one managed container has the same runtime identity.");
        }

        var columns = lines[0].Split('\t');
        if (columns.Length != 4 || string.IsNullOrWhiteSpace(columns[3]))
        {
            throw Failure("runtime.inspect.invalid", "Docker returned invalid managed runtime metadata.");
        }

        return new RuntimeResource(runtimeId, columns[3], columns[0], columns[1], columns[2]);
    }

    /// <inheritdoc />
    public async Task<RuntimeResource> StartAsync(string runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await this.RequireAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        await this.ExecuteAsync(["container", "start", runtime.ContainerId], cancellationToken)
            .ConfigureAwait(false);
        return (await this.ReadAsync(runtimeId, cancellationToken).ConfigureAwait(false))
            ?? throw Failure("runtime.disappeared", "The runtime disappeared after it was started.");
    }

    /// <inheritdoc />
    public async Task<RuntimeResource> StopAsync(string runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await this.RequireAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        await this.ExecuteAsync(["container", "stop", "--time", "15", runtime.ContainerId], cancellationToken)
            .ConfigureAwait(false);
        return (await this.ReadAsync(runtimeId, cancellationToken).ConfigureAwait(false))
            ?? throw Failure("runtime.disappeared", "The runtime disappeared after it was stopped.");
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await this.RequireAsync(runtimeId, cancellationToken).ConfigureAwait(false);
        await this.ExecuteAsync(["container", "rm", "--force", runtime.ContainerId], cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RuntimeResource> RequireAsync(
        string runtimeId,
        CancellationToken cancellationToken)
    {
        return await this.ReadAsync(runtimeId, cancellationToken).ConfigureAwait(false)
            ?? throw Failure("runtime.not_found", "The managed runtime does not exist.");
    }

    private async Task<string> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(this.dockerExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw Failure("runtime.docker.unavailable", "The Docker process could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        if (output.Length > MaximumOutputLength || error.Length > MaximumOutputLength)
        {
            throw Failure("runtime.docker.output_limit", "Docker returned more output than the safety limit.");
        }

        if (process.ExitCode != 0)
        {
            throw Failure(
                "runtime.docker.failed",
                string.IsNullOrWhiteSpace(error)
                    ? "Docker rejected the runtime operation."
                    : Sanitize(error));
        }

        return output;
    }

    private static string Sanitize(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 512 ? singleLine : singleLine[..512];
    }

    private static void ValidateRuntimeId(string runtimeId)
    {
        if (string.IsNullOrWhiteSpace(runtimeId)
            || runtimeId != runtimeId.Trim()
            || runtimeId.Length > 64
            || runtimeId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
        {
            throw Failure("runtime.id.invalid", "The runtime identifier is invalid.");
        }
    }

    private static RuntimeManagerException Failure(string code, string message) => new(code, message);
}
