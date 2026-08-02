namespace JulOS.Contracts.Runtime;

public sealed record RuntimeResourceLimits(
    int MemoryMegabytes,
    decimal CpuLimit,
    int PidsLimit);

public sealed record CreatePackageRuntimeRequest(
    string PackageId,
    string PackageVersion,
    string InstanceId,
    string Image,
    RuntimeResourceLimits Limits,
    IReadOnlyDictionary<string, string> Environment,
    bool NetworkAccess);

public sealed record PackageRuntimeResponse(
    string RuntimeId,
    string PackageId,
    string PackageVersion,
    string InstanceId,
    string Image,
    string State,
    DateTimeOffset ObservedAtUtc);

public sealed record RuntimeLogResponse(
    string RuntimeId,
    IReadOnlyList<string> Lines,
    bool Truncated);
