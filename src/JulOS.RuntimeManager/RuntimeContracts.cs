namespace JulOS.RuntimeManager;

public sealed record RuntimeVolumeRequest(string Name, string Target, bool ReadOnly);

public sealed record RuntimeCreateRequest(
    string RuntimeId,
    string PackageId,
    string Image,
    decimal CpuLimit,
    int MemoryLimitMb,
    IReadOnlyList<string> Networks,
    IReadOnlyList<RuntimeVolumeRequest> Volumes,
    IReadOnlyDictionary<string, string> Environment);

public sealed record RuntimeResource(
    string RuntimeId,
    string PackageId,
    string ContainerId,
    string State,
    string Image);

public sealed record RuntimeError(string Code, string Message);

public sealed class RuntimeManagerException : Exception
{
    public RuntimeManagerException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    public string Code { get; }
}

public interface IRuntimeBackend
{
    Task<RuntimeResource> CreateAsync(RuntimeCreateRequest request, CancellationToken cancellationToken);

    Task<RuntimeResource?> ReadAsync(string runtimeId, CancellationToken cancellationToken);

    Task<RuntimeResource> StartAsync(string runtimeId, CancellationToken cancellationToken);

    Task<RuntimeResource> StopAsync(string runtimeId, CancellationToken cancellationToken);

    Task RemoveAsync(string runtimeId, CancellationToken cancellationToken);
}
