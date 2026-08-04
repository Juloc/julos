namespace JulOS.Contracts.Runtime;

/// <summary>Resource limits applied to a package runtime.</summary>
/// <param name="MemoryMegabytes">Maximum runtime memory in MiB.</param>
/// <param name="CpuLimit">Maximum CPU allocation.</param>
/// <param name="PidsLimit">Maximum process count.</param>
public sealed record RuntimeResourceLimits(
    int MemoryMegabytes,
    decimal CpuLimit,
    int PidsLimit);

/// <summary>Requests creation of one package-owned runtime.</summary>
/// <param name="PackageId">Stable package identity.</param>
/// <param name="PackageVersion">Installed package version.</param>
/// <param name="InstanceId">Package-scoped stable runtime instance identity.</param>
/// <param name="Image">Immutable container image reference.</param>
/// <param name="Limits">Enforced resource limits.</param>
/// <param name="Environment">Non-secret allowlisted environment values.</param>
/// <param name="Networks">Exact Runtime Manager allowlisted networks.</param>
public sealed record CreatePackageRuntimeRequest(
    string PackageId,
    string PackageVersion,
    string InstanceId,
    string Image,
    RuntimeResourceLimits Limits,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> Networks);

/// <summary>Describes one managed package runtime.</summary>
/// <param name="RuntimeId">Runtime Manager identity.</param>
/// <param name="PackageId">Owning package identity.</param>
/// <param name="PackageVersion">Owning package version.</param>
/// <param name="InstanceId">Package-scoped instance identity.</param>
/// <param name="Image">Immutable image reference.</param>
/// <param name="State">Observed runtime state.</param>
/// <param name="ObservedAtUtc">Time at which the state was observed.</param>
public sealed record PackageRuntimeResponse(
    string RuntimeId,
    string PackageId,
    string PackageVersion,
    string InstanceId,
    string Image,
    string State,
    DateTimeOffset ObservedAtUtc);

/// <summary>Caller-safe Runtime Manager error.</summary>
/// <param name="Code">Stable machine-readable code.</param>
/// <param name="Message">Caller-safe explanation.</param>
public sealed record RuntimeManagerErrorResponse(
    string Code,
    string Message);

/// <summary>Bounded log output from one package runtime.</summary>
/// <param name="RuntimeId">Runtime identity.</param>
/// <param name="Lines">Sanitized bounded log lines.</param>
/// <param name="Truncated">Whether additional lines were omitted.</param>
public sealed record RuntimeLogResponse(
    string RuntimeId,
    IReadOnlyList<string> Lines,
    bool Truncated);
