using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace JulOS.RuntimeManager;

/// <summary>
/// Owns only containers carrying the JulOS managed and runtime labels. All Docker
/// arguments are added without a shell and every create request passes <see cref="RuntimePolicy"/>.
/// </summary>
public sealed class DockerCliRuntimeBackend : IRuntimeBackend
{
    private const string ManagedLabel = "com.juloc.julos.managed=true";
    private const string PackageVersionLabelName = "com.juloc.julos.package-version";
    private const string InstanceLabelName = "com.juloc.julos.instance";
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

        var arguments = CreateArguments(request);
        string? secretEnvironmentFile = null;
        try
        {
            if (request.SecretEnvironment.Count > 0)
            {
                secretEnvironmentFile = await WriteSecretEnvironmentAsync(
                    request.SecretEnvironment,
                    cancellationToken).ConfigureAwait(false);
                arguments.Add("--env-file");
                arguments.Add(secretEnvironmentFile);
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
                request.PackageVersion,
                request.InstanceId,
                containerId,
                "created",
                request.Image);
        }
        finally
        {
            DeleteSecretEnvironmentFile(secretEnvironmentFile);
        }
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
                "{{.ID}}\t{{.State}}\t{{.Image}}\t{{.Label \"com.juloc.julos.package\"}}"
                + $"\t{{{{.Label \"{PackageVersionLabelName}\"}}}}"
                + $"\t{{{{.Label \"{InstanceLabelName}\"}}}}",
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
        if (columns.Length != 6
            || string.IsNullOrWhiteSpace(columns[3])
            || string.IsNullOrWhiteSpace(columns[4])
            || string.IsNullOrWhiteSpace(columns[5]))
        {
            throw Failure("runtime.inspect.invalid", "Docker returned invalid managed runtime metadata.");
        }

        return new RuntimeResource(
            runtimeId,
            columns[3],
            columns[4],
            columns[5],
            columns[0],
            columns[1],
            columns[2]);
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

    private static List<string> CreateArguments(RuntimeCreateRequest request)
    {
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
            "--label",
            $"{PackageVersionLabelName}={request.PackageVersion}",
            "--label",
            $"{InstanceLabelName}={request.InstanceId}",
            "--cpus",
            request.CpuLimit.ToString(CultureInfo.InvariantCulture),
            "--memory",
            $"{request.MemoryLimitMb.ToString(CultureInfo.InvariantCulture)}m",
            "--pids-limit",
            request.PidsLimit.ToString(CultureInfo.InvariantCulture),
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

        return arguments;
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

    private static async Task<string> WriteSecretEnvironmentAsync(
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"julos-runtime-{Guid.NewGuid():N}.env");
        try
        {
            await using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.NewLine = "\n";
                foreach (var pair in environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    await writer.WriteLineAsync($"{pair.Key}={pair.Value}".AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return path;
        }
        catch
        {
            DeleteSecretEnvironmentFile(path);
            throw;
        }
    }

    private static void DeleteSecretEnvironmentFile(string? path)
    {
        if (path is null)
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The temporary file contains only an expiring runtime credential and is never reused.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best effort; directory permissions still restrict the temporary file.
        }
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
