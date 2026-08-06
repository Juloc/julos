namespace JulOS.Contracts.Remote;

/// <summary>Stable JulOS 1.0 capability identity and operation names for Remote sessions.</summary>
public static class RemoteSessionCapabilityContract
{
    /// <summary>Capability identity required by the Remote package.</summary>
    public const string Name = "remote.session";

    /// <summary>Current Remote session capability contract version.</summary>
    public const string Version = "1.0.0";

    /// <summary>Creates an idempotent Remote session request.</summary>
    public const string CreateOperation = "create";

    /// <summary>Reads one Remote session.</summary>
    public const string ReadOperation = "read";

    /// <summary>Lists bounded Remote sessions.</summary>
    public const string ListOperation = "list";

    /// <summary>Cancels one Remote session.</summary>
    public const string CancelOperation = "cancel";
}

/// <summary>Stable lifecycle states shared by all Remote transport implementations.</summary>
public static class RemoteSessionStates
{
    /// <summary>The request was accepted but no runtime has been allocated.</summary>
    public const string Requested = "requested";

    /// <summary>A provider runtime is being allocated.</summary>
    public const string Provisioning = "provisioning";

    /// <summary>The provider is establishing the remote protocol connection.</summary>
    public const string Connecting = "connecting";

    /// <summary>The protocol and display transport are connected.</summary>
    public const string Connected = "connected";

    /// <summary>A graceful disconnect is in progress.</summary>
    public const string Disconnecting = "disconnecting";

    /// <summary>The session ended normally.</summary>
    public const string Disconnected = "disconnected";

    /// <summary>The request was cancelled before normal completion.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The request deadline or session lifetime elapsed.</summary>
    public const string Expired = "expired";

    /// <summary>The provider failed with a caller-safe failure.</summary>
    public const string Failed = "failed";

    /// <summary>Returns whether one state is terminal.</summary>
    /// <param name="state">Session state.</param>
    /// <returns>Whether no later state transition is allowed.</returns>
    public static bool IsTerminal(string state) =>
        string.Equals(state, Disconnected, StringComparison.Ordinal)
        || string.Equals(state, Cancelled, StringComparison.Ordinal)
        || string.Equals(state, Expired, StringComparison.Ordinal)
        || string.Equals(state, Failed, StringComparison.Ordinal);
}

/// <summary>Stable caller-safe Remote session failure codes.</summary>
public static class RemoteSessionFailureCodes
{
    /// <summary>The requested protocol identity is malformed or unavailable.</summary>
    public const string ProtocolUnsupported = "remote.protocol_unsupported";

    /// <summary>The target host or port is invalid.</summary>
    public const string TargetInvalid = "remote.target_invalid";

    /// <summary>The referenced secret is missing or unavailable.</summary>
    public const string CredentialUnavailable = "remote.credential_unavailable";

    /// <summary>The selected network profile is missing or unavailable.</summary>
    public const string NetworkProfileUnavailable = "remote.network_profile_unavailable";

    /// <summary>The absolute request deadline elapsed.</summary>
    public const string RequestExpired = "remote.request_expired";

    /// <summary>No compatible provider runtime is available.</summary>
    public const string RuntimeUnavailable = "remote.runtime_unavailable";

    /// <summary>The remote endpoint certificate or host key is not trusted.</summary>
    public const string TrustRequired = "remote.trust_required";

    /// <summary>The remote endpoint rejected authentication.</summary>
    public const string AuthenticationFailed = "remote.authentication_failed";

    /// <summary>The connected endpoint closed unexpectedly.</summary>
    public const string ConnectionLost = "remote.connection_lost";

    /// <summary>The requested state transition is invalid.</summary>
    public const string StateTransitionInvalid = "remote.state_transition_invalid";
}

/// <summary>Protocol-neutral network target.</summary>
/// <param name="Host">DNS name or IP address without scheme, path or credentials.</param>
/// <param name="Port">Explicit TCP port from 1 through 65535.</param>
public sealed record RemoteTargetContract(
    string Host,
    int Port);

/// <summary>Requested graphical or terminal viewport.</summary>
/// <param name="Width">Viewport width in CSS pixels.</param>
/// <param name="Height">Viewport height in CSS pixels.</param>
/// <param name="DeviceScaleFactor">Client device scale factor.</param>
public sealed record RemoteViewportContract(
    int Width,
    int Height,
    decimal DeviceScaleFactor);

/// <summary>Creates one protocol-neutral Remote session.</summary>
/// <param name="OperationKey">Caller-owned idempotency key.</param>
/// <param name="Protocol">Lowercase package-defined protocol identity.</param>
/// <param name="Target">Explicit remote network target.</param>
/// <param name="SecretReferenceId">Secret-reference identity; no secret material is embedded.</param>
/// <param name="ProfileId">Optional saved Remote profile identity.</param>
/// <param name="NetworkProfileId">Optional saved network-profile identity.</param>
/// <param name="Viewport">Requested initial viewport.</param>
/// <param name="IdleTimeoutSeconds">Idle disconnect threshold.</param>
/// <param name="MaximumSessionSeconds">Absolute maximum session duration.</param>
/// <param name="RequestedAtUtc">Caller request timestamp.</param>
/// <param name="DeadlineUtc">Absolute deadline for accepting the request.</param>
public sealed record CreateRemoteSessionRequest(
    string OperationKey,
    string Protocol,
    RemoteTargetContract Target,
    Guid SecretReferenceId,
    Guid? ProfileId,
    Guid? NetworkProfileId,
    RemoteViewportContract Viewport,
    int IdleTimeoutSeconds,
    int MaximumSessionSeconds,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset DeadlineUtc);

/// <summary>Reads one Remote session.</summary>
/// <param name="SessionId">Stable session identity.</param>
public sealed record ReadRemoteSessionRequest(Guid SessionId);

/// <summary>Lists a bounded page of Remote sessions.</summary>
/// <param name="States">Optional state filter.</param>
/// <param name="Limit">Maximum number of sessions to return.</param>
/// <param name="Cursor">Optional opaque continuation cursor.</param>
public sealed record ListRemoteSessionsRequest(
    IReadOnlyList<string> States,
    int Limit,
    string? Cursor);

/// <summary>Cancels one Remote session idempotently.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="OperationKey">Caller-owned cancellation idempotency key.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
/// <param name="Reason">Optional caller-safe cancellation reason.</param>
public sealed record CancelRemoteSessionRequest(
    Guid SessionId,
    string OperationKey,
    long ExpectedRevision,
    string? Reason);

/// <summary>Caller-safe failure attached to a terminal Remote session.</summary>
/// <param name="Code">Stable failure code.</param>
/// <param name="Detail">Bounded caller-safe detail without provider exception text.</param>
/// <param name="Retryable">Whether a new session request may succeed without configuration changes.</param>
public sealed record RemoteSessionFailureResponse(
    string Code,
    string Detail,
    bool Retryable);

/// <summary>Protocol-neutral display or terminal transport descriptor.</summary>
/// <param name="Kind">Either <c>graphical</c> or <c>terminal</c>.</param>
/// <param name="ContractVersion">Display transport contract version.</param>
/// <param name="Endpoint">Authenticated same-origin relative endpoint without access tokens.</param>
/// <param name="ExpiresAtUtc">Descriptor expiry.</param>
public sealed record RemoteDisplayTransportResponse(
    string Kind,
    string ContractVersion,
    string Endpoint,
    DateTimeOffset ExpiresAtUtc);

/// <summary>Protocol-neutral Remote session snapshot.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="OperationKey">Original create operation key.</param>
/// <param name="RequestIdentity">Canonical create-request digest used for exact idempotency.</param>
/// <param name="Protocol">Package-defined protocol identity.</param>
/// <param name="Target">Caller-visible target without secrets.</param>
/// <param name="State">One lifecycle state from <see cref="RemoteSessionStates"/>.</param>
/// <param name="CreatedAtUtc">Durable creation timestamp.</param>
/// <param name="ConnectedAtUtc">Connection timestamp when reached.</param>
/// <param name="EndedAtUtc">Terminal timestamp when reached.</param>
/// <param name="Display">Display transport descriptor while available.</param>
/// <param name="Failure">Caller-safe terminal failure when applicable.</param>
/// <param name="Revision">Optimistic concurrency revision.</param>
public sealed record RemoteSessionResponse(
    Guid SessionId,
    string OperationKey,
    string RequestIdentity,
    string Protocol,
    RemoteTargetContract Target,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? EndedAtUtc,
    RemoteDisplayTransportResponse? Display,
    RemoteSessionFailureResponse? Failure,
    long Revision);

/// <summary>Bounded Remote session list page.</summary>
/// <param name="Sessions">Session snapshots.</param>
/// <param name="NextCursor">Opaque continuation cursor when another page exists.</param>
public sealed record RemoteSessionListResponse(
    IReadOnlyList<RemoteSessionResponse> Sessions,
    string? NextCursor);
