using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using JulOS.Contracts.Runtime;

namespace JulOS.RuntimeManager;

internal sealed record RuntimeManagerOptions(
    string ApiKey,
    string DockerExecutable,
    string RuntimeNetwork,
    int MaximumLogLines)
{
    internal static RuntimeManagerOptions Read(IConfiguration configuration)
    {
        var apiKey = configuration["RuntimeManager:ApiKey"]
            ?? Environment.GetEnvironmentVariable("JULOS_RUNTIME_MANAGER_KEY")
            ?? throw new InvalidOperationException("Runtime Manager API key is not configured.");
        if (Encoding.UTF8.GetByteCount(apiKey) < 32)
        {
            throw new InvalidOperationException("Runtime Manager API key must contain at least 32 UTF-8 bytes.");
        }

        var executable = configuration["RuntimeManager:DockerExecutable"] ?? "docker";
        var network = configuration["RuntimeManager:Network"] ?? "julos-runtime";
        var maximumLogLines = configuration.GetValue("RuntimeManager:MaximumLogLines", 500);
        if (maximumLogLines is < 1 or > 10_000)
        {
            throw new InvalidOperationException("Runtime Manager log limit is invalid.");
        }

        return new RuntimeManagerOptions(apiKey, executable, network, maximumLogLines);
    }
}

internal sealed class RuntimeManagerAuthenticationMiddleware
{
    private const string HeaderName = "X-JulOS-Runtime-Key";
    private readonly RequestDelegate next;
    private readonly byte[] expectedHash;

    public RuntimeManagerAuthenticationMiddleware(RequestDelegate next, RuntimeManagerOptions options)
    {
        this.next = next;
        this.expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.ApiKey));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health/live"))
        {
            await this.next(context).ConfigureAwait(false);
            return;
        }

        var supplied = context.Request.Headers[HeaderName].ToString();
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        if (!CryptographicOperations.FixedTimeEquals(this.expectedHash, suppliedHash))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "runtime.unauthorized",
                detail = "Runtime Manager authentication failed.",
            }).ConfigureAwait(false);
            return;
        }

        await this.next(context).ConfigureAwait(false);
    }
}

internal interface IPackageRuntimeManager
{
    Task<PackageRuntimeResponse> CreateAsync(
        CreatePackageRuntimeRequest request,
        CancellationToken cancellationToken);

    Task<PackageRuntimeResponse> ReadAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken);

    Task<PackageRuntimeResponse> StopAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken);

    Task<RuntimeLogResponse> ReadLogsAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken);
}

internal sealed partial class DockerPackageRuntimeManager : IPackageRuntimeManager
{
    private const string ManagedLabel = "julos.managed=true";
    private readonly RuntimeManagerOptions options;
    private readonly TimeProvider timeProvider;

    public DockerPackageRuntimeManager(RuntimeManagerOptions options, TimeProvider timeProvider)
    {
        this.options = options;
        this.timeProvider = timeProvider;
    }

    public async Task<PackageRuntimeResponse> CreateAsync(
        CreatePackageRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var runtimeName = RuntimeName(request.PackageId, request.InstanceId);
        var existing = await TryInspectAsync(
            request.PackageId,
            request.InstanceId,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.Image, request.Image, StringComparison.Ordinal))
            {
                throw new RuntimeManagerException(
                    "runtime.identity_conflict",
                    "An existing runtime identity uses a different image.");
            }
            return existing;
        }

        var arguments = new List<string>
        {
            "run",
            "--detach",
            "--name", runtimeName,
            "--label", ManagedLabel,
            "--label", $"julos.package.id={request.PackageId}",
            "--label", $"julos.package.version={request.PackageVersion}",
            "--label", $"julos.runtime.instance={request.InstanceId}",
            "--memory", $"{request.Limits.MemoryMegabytes}m",
            "--cpus", request.Limits.CpuLimit.ToString(CultureInfo.InvariantCulture),
            "--pids-limit", request.Limits.PidsLimit.ToString(CultureInfo.InvariantCulture),
            "--read-only",
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges:true",
            "--restart", "no",
        };
        if (request.NetworkAccess)
        {
            arguments.AddRange(["--network", this.options.RuntimeNetwork]);
        }
        else
        {
            arguments.AddRange(["--network", "none"]);
        }
        foreach (var variable in request.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            arguments.AddRange(["--env", $"{variable.Key}={variable.Value}"]);
        }
        arguments.Add(request.Image);

        _ = await RunDockerAsync(arguments, cancellationToken).ConfigureAwait(false);
        return await ReadAsync(request.PackageId, request.InstanceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackageRuntimeResponse> ReadAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken) =>
        await TryInspectAsync(packageId, instanceId, cancellationToken).ConfigureAwait(false)
        ?? throw new RuntimeManagerException("runtime.not_found", "The package runtime does not exist.");

    public async Task<PackageRuntimeResponse> StopAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        _ = await ReadAsync(packageId, instanceId, cancellationToken).ConfigureAwait(false);
        _ = await RunDockerAsync(
            ["stop", "--time", "15", RuntimeName(packageId, instanceId)],
            cancellationToken).ConfigureAwait(false);
        return await ReadAsync(packageId, instanceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        _ = await ReadAsync(packageId, instanceId, cancellationToken).ConfigureAwait(false);
        _ = await RunDockerAsync(
            ["rm", "--force", RuntimeName(packageId, instanceId)],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RuntimeLogResponse> ReadLogsAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var runtime = await ReadAsync(packageId, instanceId, cancellationToken).ConfigureAwait(false);
        var output = await RunDockerAsync(
            ["logs", "--tail", this.options.MaximumLogLines.ToString(CultureInfo.InvariantCulture), runtime.RuntimeId],
            cancellationToken,
            allowStandardError: true).ConfigureAwait(false);
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return new RuntimeLogResponse(
            runtime.RuntimeId,
            lines.Take(this.options.MaximumLogLines).ToArray(),
            lines.Length >= this.options.MaximumLogLines);
    }

    private async Task<PackageRuntimeResponse?> TryInspectAsync(
        string packageId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(packageId, instanceId);
        var name = RuntimeName(packageId, instanceId);
        string json;
        try
        {
            json = await RunDockerAsync(["inspect", name], cancellationToken).ConfigureAwait(false);
        }
        catch (RuntimeManagerException exception) when (exception.Code == "runtime.docker_failed")
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement[0];
        var labels = root.GetProperty("Config").GetProperty("Labels");
        if (!string.Equals(labels.GetProperty("julos.managed").GetString(), "true", StringComparison.Ordinal)
            || !string.Equals(labels.GetProperty("julos.package.id").GetString(), packageId, StringComparison.Ordinal)
            || !string.Equals(labels.GetProperty("julos.runtime.instance").GetString(), instanceId, StringComparison.Ordinal))
        {
            throw new RuntimeManagerException(
                "runtime.ownership_mismatch",
                "The Docker object is not owned by the requested JulOS package runtime.");
        }

        return new PackageRuntimeResponse(
            root.GetProperty("Name").GetString()?.TrimStart('/') ?? name,
            packageId,
            labels.GetProperty("julos.package.version").GetString() ?? "unknown",
            instanceId,
            root.GetProperty("Config").GetProperty("Image").GetString() ?? "unknown",
            root.GetProperty("State").GetProperty("Status").GetString() ?? "unknown",
            this.timeProvider.GetUtcNow());
    }

    private async Task<string> RunDockerAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool allowStandardError = false)
    {
        var start = new ProcessStartInfo
        {
            FileName = this.options.DockerExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new RuntimeManagerException("runtime.docker_start_failed", "Docker process could not start.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0 && !(allowStandardError && output.Length > 0))
        {
            throw new RuntimeManagerException(
                "runtime.docker_failed",
                string.IsNullOrWhiteSpace(error) ? "Docker operation failed." : error.Trim());
        }
        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private static void Validate(CreatePackageRuntimeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentity(request.PackageId, request.InstanceId);
        if (!SemanticVersion().IsMatch(request.PackageVersion)
            || !PinnedImage().IsMatch(request.Image)
            || request.Limits.MemoryMegabytes is < 16 or > 32768
            || request.Limits.CpuLimit is <= 0 or > 32
            || request.Limits.PidsLimit is < 16 or > 65536
            || request.Environment.Count > 128)
        {
            throw new RuntimeManagerException("runtime.request_invalid", "Runtime request is invalid.");
        }
        foreach (var pair in request.Environment)
        {
            if (!EnvironmentName().IsMatch(pair.Key)
                || pair.Value.Length > 8192
                || pair.Value.Any(char.IsControl))
            {
                throw new RuntimeManagerException("runtime.environment_invalid", "Runtime environment is invalid.");
            }
        }
    }

    private static void ValidateIdentity(string packageId, string instanceId)
    {
        if (!PackageId().IsMatch(packageId) || !InstanceId().IsMatch(instanceId))
        {
            throw new RuntimeManagerException("runtime.identity_invalid", "Runtime identity is invalid.");
        }
    }

    private static string RuntimeName(string packageId, string instanceId) =>
        $"julos-{packageId.Replace('.', '-')}-{instanceId}";

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageId();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceId();

    [GeneratedRegex("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();

    [GeneratedRegex("^[a-z0-9./_-]+@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PinnedImage();

    [GeneratedRegex("^[A-Z_][A-Z0-9_]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentName();
}

internal sealed class RuntimeManagerException : Exception
{
    public RuntimeManagerException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    public string Code { get; }
}
