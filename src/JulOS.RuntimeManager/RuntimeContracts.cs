namespace JulOS.RuntimeManager;

/// <summary>One package-owned named-volume mount.</summary>
/// <param name="Name">Package-owned Docker volume name.</param>
/// <param name="Target">Absolute container target path.</param>
/// <param name="ReadOnly">Whether the mount is read-only.</param>
public sealed record RuntimeVolumeRequest(string Name, string Target, bool ReadOnly);

/// <summary>Creates one isolated package runtime.</summary>
/// <param name="RuntimeId">Stable managed runtime identity.</param>
/// <param name="PackageId">Owning package identity.</param>
/// <param name="Image">Immutable digest-pinned image reference.</param>
/// <param name="CpuLimit">Maximum CPU allocation.</param>
/// <param name="MemoryLimitMb">Maximum memory in MiB.</param>
/// <param name="Networks">Allowlisted networks.</param>
/// <param name="Volumes">Package-owned named-volume mounts.</param>
/// <param name="Environment">Validated non-secret environment values.</param>
public sealed record RuntimeCreateRequest(
    string RuntimeId,
    string PackageId,
    string Image,
    decimal CpuLimit,
    int MemoryLimitMb,
    IReadOnlyList<string> Networks,
    IReadOnlyList<RuntimeVolumeRequest> Volumes,
    IReadOnlyDictionary<string, string> Environment);

/// <summary>Observed state of one managed runtime.</summary>
/// <param name="RuntimeId">Managed runtime identity.</param>
/// <param name="PackageId">Owning package identity.</param>
/// <param name="ContainerId">Docker container identity.</param>
/// <param name="State">Observed Docker state.</param>
/// <param name="Image">Immutable image reference.</param>
public sealed record RuntimeResource(
    string RuntimeId,
    string PackageId,
    string ContainerId,
    string State,
    string Image);

/// <summary>Caller-safe Runtime Manager error response.</summary>
/// <param name="Code">Stable machine-readable error code.</param>
/// <param name="Message">Caller-safe explanation.</param>
public sealed record RuntimeError(string Code, string Message);

/// <summary>Stable Runtime Manager policy or Docker-operation failure.</summary>
public sealed class RuntimeManagerException : Exception
{
    /// <summary>Creates a Runtime Manager failure.</summary>
    /// <param name="code">Stable machine-readable error code.</param>
    /// <param name="message">Caller-safe explanation.</param>
    public RuntimeManagerException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable machine-readable error code.</summary>
    public string Code { get; }
}

/// <summary>Narrow backend boundary for JulOS-owned package runtimes.</summary>
public interface IRuntimeBackend
{
    /// <summary>Creates one validated package runtime.</summary>
    Task<RuntimeResource> CreateAsync(RuntimeCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Reads one managed runtime without inspecting unrelated containers.</summary>
    Task<RuntimeResource?> ReadAsync(string runtimeId, CancellationToken cancellationToken);

    /// <summary>Starts one managed runtime.</summary>
    Task<RuntimeResource> StartAsync(string runtimeId, CancellationToken cancellationToken);

    /// <summary>Stops one managed runtime.</summary>
    Task<RuntimeResource> StopAsync(string runtimeId, CancellationToken cancellationToken);

    /// <summary>Removes one managed runtime.</summary>
    Task RemoveAsync(string runtimeId, CancellationToken cancellationToken);
}
