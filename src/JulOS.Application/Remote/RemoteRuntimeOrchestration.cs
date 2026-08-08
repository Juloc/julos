using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;

namespace JulOS.Application.Remote;

/// <summary>Allowlisted provider runtime selected for one protocol.</summary>
/// <param name="Protocol">Package-defined protocol identity.</param>
/// <param name="ProviderPackageId">Package identity owning the runtime.</param>
/// <param name="PackageVersion">Installed provider version.</param>
/// <param name="Image">Immutable digest-pinned provider image.</param>
/// <param name="Limits">Runtime resource limits.</param>
public sealed record RemoteProviderRuntimeDefinition(
    string Protocol,
    string ProviderPackageId,
    string PackageVersion,
    string Image,
    RuntimeResourceLimits Limits);

/// <summary>Approved target and runtime-network boundary for Remote runtime egress.</summary>
/// <param name="NetworkProfileId">Stable profile identity.</param>
/// <param name="Default">Whether this profile is selected when a request omits an identity.</param>
/// <param name="RuntimeNetworks">Exact Runtime Manager network allowlist entries.</param>
/// <param name="AllowedTargetPatterns">Exact hosts, <c>*.suffix</c> patterns or narrow <c>label-*</c> runtime prefixes.</param>
/// <param name="AllowedPorts">Explicit target TCP ports.</param>
public sealed record RemoteNetworkProfileDefinition(
    Guid NetworkProfileId,
    bool Default,
    IReadOnlyList<string> RuntimeNetworks,
    IReadOnlyList<string> AllowedTargetPatterns,
    IReadOnlyList<int> AllowedPorts);

/// <summary>Fully authorized provider and egress selection.</summary>
/// <param name="Provider">Selected protocol provider.</param>
/// <param name="NetworkProfile">Selected target/network profile.</param>
public sealed record RemoteRuntimeSelection(
    RemoteProviderRuntimeDefinition Provider,
    RemoteNetworkProfileDefinition NetworkProfile);

/// <summary>Resolves only configured provider images, target rules and runtime networks.</summary>
public interface IRemoteRuntimePolicy
{
    /// <summary>Authorizes and resolves one protocol, target and optional network profile.</summary>
    RemoteRuntimeSelection Resolve(
        string protocol,
        Guid? networkProfileId,
        RemoteTargetContract target);
}

/// <summary>Narrow authenticated client for JulOS-owned Remote runtime allocation.</summary>
public interface IRemoteRuntimeManager
{
    /// <summary>Creates or recovers and starts one exact managed runtime.</summary>
    Task<PackageRuntimeResponse> AllocateAndStartAsync(
        CreatePackageRuntimeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one managed runtime idempotently.</summary>
    Task RemoveAsync(
        string runtimeId,
        CancellationToken cancellationToken = default);
}

/// <summary>Requests idempotent allocation for one durable Remote session.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="SessionId">Durable session identity.</param>
/// <param name="ExpectedRevision">Expected requested-session revision.</param>
public sealed record ProvisionRemoteSessionCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    Guid SessionId,
    long ExpectedRevision);

/// <summary>Allocates and assigns one allowlisted provider runtime to a durable Remote session.</summary>
public interface IRemoteSessionProvisioner
{
    /// <summary>Allocates or recovers one exact runtime and advances the session to connecting.</summary>
    Task<RemoteSessionResponse> ProvisionAsync(
        ProvisionRemoteSessionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable configured Remote runtime policy failure.</summary>
public sealed class RemoteRuntimePolicyException : Exception
{
    /// <summary>Creates a caller-safe policy refusal.</summary>
    public RemoteRuntimePolicyException(string code, string message)
        : base(message)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; }
}

/// <summary>Stable Runtime Manager transport or identity failure.</summary>
public sealed class RemoteRuntimeManagerException : Exception
{
    /// <summary>Creates a caller-safe Runtime Manager failure.</summary>
    public RemoteRuntimeManagerException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        this.Code = code;
    }

    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; }
}
