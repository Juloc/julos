using System.Text.Json;

using JulOS.Contracts.Remote;
using JulOS.Contracts.Runtime;

namespace JulOS.PackageSdk;

/// <summary>Stable capability used by packages that need one isolated interactive runtime.</summary>
public static class InteractiveSessionCapabilityContract
{
    /// <summary>Capability identity.</summary>
    public const string Name = "interactive.session";

    /// <summary>Current capability version.</summary>
    public const string Version = "1.0.0";

    /// <summary>Creates or recovers one idempotent interactive session.</summary>
    public const string CreateOperation = "create";

    /// <summary>Reads one interactive session owned by the caller.</summary>
    public const string ReadOperation = "read";

    /// <summary>Terminates one interactive session and releases its runtime.</summary>
    public const string TerminateOperation = "terminate";
}

/// <summary>Standard private worker command used to resolve package-owned runtime policy.</summary>
public static class InteractiveSessionWorkerCommands
{
    /// <summary>Resolves opaque package input into one validated runtime plan.</summary>
    public const string ResolvePlan = "interactive.resolve-plan";
}

/// <summary>Creates one interactive package runtime session.</summary>
/// <param name="OperationKey">Caller-owned idempotency key.</param>
/// <param name="Request">Opaque package-defined request passed only to the caller's own worker.</param>
public sealed record CreateInteractiveSessionRequest(
    string OperationKey,
    JsonElement Request);

/// <summary>Trusted request delivered by Core only to the caller's own package worker.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="Request">Interactive session request.</param>
public sealed record ResolveInteractiveSessionPlanRequest(
    Guid OwnerUserId,
    CreateInteractiveSessionRequest Request);

/// <summary>One non-persisted secret required by both the runtime and its presentation provider.</summary>
/// <param name="EnvironmentName">Runtime secret-environment key.</param>
/// <param name="Value">Secret value transported only over the private worker channel.</param>
public sealed record InteractiveSessionCredential(
    string EnvironmentName,
    string Value);

/// <summary>Generic package-owned runtime plan executed by Core through existing platform boundaries.</summary>
/// <param name="PackageVersion">Installed caller package version.</param>
/// <param name="Image">Immutable digest-pinned runtime image.</param>
/// <param name="Limits">Runtime resource limits.</param>
/// <param name="Environment">Non-secret runtime environment.</param>
/// <param name="RuntimeNetwork">Exact Runtime Manager network.</param>
/// <param name="Volumes">Package-owned runtime volumes.</param>
/// <param name="PresentationProtocol">Protocol identity resolved through the Remote capability boundary.</param>
/// <param name="PresentationPort">Internal runtime presentation port.</param>
/// <param name="Credential">Presentation credential injected into the runtime and stored as a secret reference.</param>
/// <param name="Viewport">Initial presentation viewport.</param>
/// <param name="IdleTimeoutSeconds">Idle disconnect threshold.</param>
/// <param name="MaximumSessionSeconds">Absolute maximum session duration.</param>
public sealed record InteractiveSessionRuntimePlan(
    string PackageVersion,
    string Image,
    RuntimeResourceLimits Limits,
    IReadOnlyDictionary<string, string> Environment,
    string RuntimeNetwork,
    IReadOnlyList<PackageRuntimeVolume> Volumes,
    string PresentationProtocol,
    int PresentationPort,
    InteractiveSessionCredential Credential,
    RemoteViewportContract Viewport,
    int IdleTimeoutSeconds,
    int MaximumSessionSeconds);

/// <summary>Reads one interactive package session.</summary>
/// <param name="SessionId">Stable session identity.</param>
public sealed record ReadInteractiveSessionRequest(Guid SessionId);

/// <summary>Terminates one interactive package session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
public sealed record TerminateInteractiveSessionRequest(Guid SessionId, long ExpectedRevision);

/// <summary>Caller-safe interactive session snapshot without internal runtime target information.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="State">Current session state.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
/// <param name="ConnectedAtUtc">Connection timestamp when reached.</param>
/// <param name="EndedAtUtc">Terminal timestamp when reached.</param>
/// <param name="Display">Same-origin presentation descriptor while available.</param>
/// <param name="Failure">Caller-safe failure when applicable.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
public sealed record InteractiveSessionResponse(
    Guid SessionId,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? EndedAtUtc,
    RemoteDisplayTransportResponse? Display,
    RemoteSessionFailureResponse? Failure,
    long Revision);
