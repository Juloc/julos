namespace JulOS.Contracts.HostConnectors;

/// <summary>
/// The stable Host Connector failure codes from <c>docs/HOST_CONNECTOR.md</c> section 13.
/// </summary>
/// <remarks>
/// These codes are part of the public contract: callers branch on them, so a code is added
/// but never renamed or repurposed. Package adapters declare their own codes and never
/// return a raw host error or captured command output as safe detail.
/// </remarks>
public static class HostConnectorErrorCodes
{
    /// <summary>No Host Connector with the requested identity exists.</summary>
    public const string NotFound = "host_connector.not_found";

    /// <summary>The Host Connector is enrolled but not currently reachable.</summary>
    public const string Offline = "host_connector.offline";

    /// <summary>The credential was terminally revoked.</summary>
    public const string Revoked = "host_connector.revoked";

    /// <summary>The protocol version is missing or not accepted; neither side downgrades.</summary>
    public const string ProtocolIncompatible = "host_connector.protocol_incompatible";

    /// <summary>The Host Connector does not advertise the requested capability.</summary>
    public const string CapabilityUnavailable = "host_connector.capability_unavailable";

    /// <summary>The request envelope failed validation before execution.</summary>
    public const string RequestInvalid = "host_connector.request_invalid";

    /// <summary>The deadline passed before a result arrived.</summary>
    public const string DeadlineExceeded = "host_connector.deadline_exceeded";

    /// <summary>The request was cancelled before any Connector claimed it.</summary>
    public const string CancelledBeforeClaim = "host_connector.cancelled_before_claim";

    /// <summary>
    /// A reconcile-required operation was interrupted and its external effect is unknown.
    /// It is never replayed; a new typed read-only inspection request establishes the truth.
    /// </summary>
    public const string OutcomeUnknown = "host_connector.outcome_unknown";

    /// <summary>The canonical result exceeded the request's byte budget.</summary>
    public const string ResultTooLarge = "host_connector.result_too_large";

    /// <summary>Different result bytes were submitted after terminal completion.</summary>
    public const string ResultConflict = "host_connector.result_conflict";

    /// <summary>No valid grant authorizes the requested stream.</summary>
    public const string StreamNotAuthorized = "host_connector.stream_not_authorized";

    /// <summary>The stream grant expired.</summary>
    public const string StreamExpired = "host_connector.stream_expired";

    /// <summary>The identity-file migration could not be completed.</summary>
    public const string IdentityMigrationFailed = "host_connector.identity_migration_failed";

    /// <summary>Both identity files exist and their protected fields differ.</summary>
    public const string IdentityMigrationConflict = "host_connector.identity_migration_conflict";

    /// <summary>Queued or running legacy requests still block the upgrade.</summary>
    public const string UpgradeRequestsActive = "host_connector.upgrade_requests_active";

    /// <summary>The credential rotation could not be completed.</summary>
    public const string CredentialRotationFailed = "host_connector.credential_rotation_failed";

    /// <summary>Another rotation is already requested or in overlap.</summary>
    public const string CredentialRotationActive = "host_connector.credential_rotation_active";

    /// <summary>The rotation expired before it was attempted.</summary>
    public const string CredentialRotationExpired = "host_connector.credential_rotation_expired";

    /// <summary>Every stable code, for contract fixtures and exhaustiveness tests.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        NotFound,
        Offline,
        Revoked,
        ProtocolIncompatible,
        CapabilityUnavailable,
        RequestInvalid,
        DeadlineExceeded,
        CancelledBeforeClaim,
        OutcomeUnknown,
        ResultTooLarge,
        ResultConflict,
        StreamNotAuthorized,
        StreamExpired,
        IdentityMigrationFailed,
        IdentityMigrationConflict,
        UpgradeRequestsActive,
        CredentialRotationFailed,
        CredentialRotationActive,
        CredentialRotationExpired,
    ];
}
