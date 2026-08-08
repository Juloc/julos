using JulOS.Contracts.Remote;

namespace JulOS.Contracts.Browser;

/// <summary>Stable JulOS 1.0 capability identity and operations for isolated Browser sessions.</summary>
public static class BrowserSessionCapabilityContract
{
    /// <summary>Capability identity required by the Browser package.</summary>
    public const string Name = "browser.session";

    /// <summary>Current Browser session capability contract version.</summary>
    public const string Version = "1.0.0";

    /// <summary>Creates or recovers one idempotent Browser session.</summary>
    public const string CreateOperation = "create";

    /// <summary>Reads one Browser session owned by the caller.</summary>
    public const string ReadOperation = "read";

    /// <summary>Terminates one Browser session and releases its runtime.</summary>
    public const string TerminateOperation = "terminate";
}

/// <summary>Private Browser package worker commands used by the trusted control plane.</summary>
public static class BrowserWorkerCommands
{
    /// <summary>Resolves user-owned profile data into a non-secret runtime plan.</summary>
    public const string ResolveSessionPlan = "browser.resolve-session-plan";
}

/// <summary>Browser profile modes accepted by runtime orchestration.</summary>
public static class BrowserSessionProfileModes
{
    /// <summary>Runtime-local profile removed with the session.</summary>
    public const string Temporary = "temporary";

    /// <summary>User-owned profile retained in a package-owned volume.</summary>
    public const string Persistent = "persistent";

    /// <summary>User-owned fixed-application profile retained in a package-owned volume.</summary>
    public const string Application = "application";
}

/// <summary>Creates one isolated Browser session.</summary>
/// <param name="OperationKey">Caller-owned idempotency key.</param>
/// <param name="InitialUrl">Absolute HTTP or HTTPS start URL.</param>
/// <param name="ProfileMode">One value from <see cref="BrowserSessionProfileModes"/>.</param>
/// <param name="ProfileId">Required for persistent and application profiles; null for temporary mode.</param>
public sealed record CreateBrowserSessionRequest(
    string OperationKey,
    string InitialUrl,
    string ProfileMode,
    Guid? ProfileId);

/// <summary>Trusted control-plane request to resolve package-owned Browser profile policy.</summary>
/// <param name="OwnerUserId">Authenticated JulOS user.</param>
/// <param name="Request">Browser session request to resolve.</param>
public sealed record ResolveBrowserSessionPlanRequest(
    Guid OwnerUserId,
    CreateBrowserSessionRequest Request);

/// <summary>Non-secret runtime plan resolved by the Browser package worker.</summary>
/// <param name="PackageVersion">Installed Browser package version.</param>
/// <param name="InitialUrl">Validated URL Chromium must open.</param>
/// <param name="RuntimeNetwork">Exact configured Runtime Manager network.</param>
/// <param name="ProfileMode">Resolved profile mode.</param>
/// <param name="ProfileId">Retained profile identity, when applicable.</param>
/// <param name="VolumeName">Package-owned persistent profile volume, when applicable.</param>
/// <param name="IdleTimeoutSeconds">Validated session idle timeout.</param>
public sealed record BrowserSessionRuntimePlan(
    string PackageVersion,
    string InitialUrl,
    string RuntimeNetwork,
    string ProfileMode,
    Guid? ProfileId,
    string? VolumeName,
    int IdleTimeoutSeconds);

/// <summary>Reads one Browser session.</summary>
/// <param name="SessionId">Stable protocol-neutral session identity.</param>
public sealed record ReadBrowserSessionRequest(Guid SessionId);

/// <summary>Terminates one Browser session.</summary>
/// <param name="SessionId">Stable protocol-neutral session identity.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
public sealed record TerminateBrowserSessionRequest(Guid SessionId, long ExpectedRevision);

/// <summary>Caller-safe Browser session snapshot. Runtime addresses and credentials are never included.</summary>
/// <param name="SessionId">Stable protocol-neutral session identity.</param>
/// <param name="State">Current session state.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
/// <param name="ConnectedAtUtc">Connection timestamp when reached.</param>
/// <param name="EndedAtUtc">Terminal timestamp when reached.</param>
/// <param name="Display">Same-origin presentation descriptor while available.</param>
/// <param name="Failure">Caller-safe failure when applicable.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
public sealed record BrowserSessionResponse(
    Guid SessionId,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? EndedAtUtc,
    RemoteDisplayTransportResponse? Display,
    RemoteSessionFailureResponse? Failure,
    long Revision);
