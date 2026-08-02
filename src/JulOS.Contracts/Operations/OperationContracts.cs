namespace JulOS.Contracts.Operations;

/// <summary>Stable operation lifecycle values returned by the control-plane API.</summary>
public static class OperationStates
{
    /// <summary>The operation is persisted and waiting for an executor.</summary>
    public const string Queued = "queued";

    /// <summary>An executor has started the operation.</summary>
    public const string Running = "running";

    /// <summary>The requested target state was verified.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The operation ended with a safe, persisted failure cause.</summary>
    public const string Failed = "failed";

    /// <summary>The operation ended without completing its requested work.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>Stable public failures owned by the operation-resource framework.</summary>
public static class OperationErrorCodes
{
    /// <summary>The submitted operation representation is invalid.</summary>
    public const string Invalid = "operation.invalid";

    /// <summary>The requested operation does not exist for the current user.</summary>
    public const string NotFound = "operation.not_found";

    /// <summary>An idempotency key was already used for a different request.</summary>
    public const string IdempotencyConflict = "operation.idempotency_conflict";

    /// <summary>The current lifecycle state does not permit the requested transition.</summary>
    public const string InvalidTransition = "operation.invalid_transition";

    /// <summary>The operation can no longer accept a cancellation request.</summary>
    public const string NotCancellable = "operation.not_cancellable";
}

/// <summary>Creates one durable background-operation resource.</summary>
/// <param name="OperationType">A stable dotted operation type owned by Core or one package.</param>
/// <param name="SourcePackageId">The optional package identity that owns the work.</param>
/// <param name="TargetReference">An opaque stable target reference without credentials.</param>
/// <param name="IdempotencyKey">A caller-stable key used to make retries safe.</param>
public sealed record CreateOperationRequest(
    string OperationType,
    string? SourcePackageId,
    string TargetReference,
    string IdempotencyKey);

/// <summary>One durable operation-resource representation.</summary>
/// <param name="OperationId">The stable operation identifier.</param>
/// <param name="OperationType">The stable operation type.</param>
/// <param name="OwnerUserId">The user who requested the operation.</param>
/// <param name="SourcePackageId">The optional package identity that owns the work.</param>
/// <param name="TargetReference">The sanitized opaque target reference.</param>
/// <param name="State">The current lifecycle state.</param>
/// <param name="ProgressPercent">The latest percentage when the executor supplied one.</param>
/// <param name="CurrentStep">The latest localizable or operator-facing step identifier.</param>
/// <param name="CreatedAtUtc">When the operation resource was created.</param>
/// <param name="StartedAtUtc">When an executor started the operation.</param>
/// <param name="CompletedAtUtc">When the terminal state was reached.</param>
/// <param name="FailureCode">A stable safe failure code for a failed operation.</param>
/// <param name="FailureDetail">A sanitized safe cause for a failed operation.</param>
/// <param name="CorrelationId">The correlation identifier of the creation request.</param>
/// <param name="CancellationRequested">Whether the owning executor must stop safely.</param>
/// <param name="Revision">The current optimistic-concurrency revision.</param>
public sealed record OperationResponse(
    Guid OperationId,
    string OperationType,
    Guid OwnerUserId,
    string? SourcePackageId,
    string TargetReference,
    string State,
    int? ProgressPercent,
    string? CurrentStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode,
    string? FailureDetail,
    string CorrelationId,
    bool CancellationRequested,
    int Revision);

/// <summary>One immutable progress event belonging to an operation.</summary>
/// <param name="EventId">The stable event identifier.</param>
/// <param name="OperationId">The owning operation identifier.</param>
/// <param name="ProgressPercent">The percentage supplied by the executor, when meaningful.</param>
/// <param name="CurrentStep">The sanitized current-step identifier.</param>
/// <param name="OccurredAtUtc">When Core accepted the event.</param>
public sealed record OperationProgressEventResponse(
    Guid EventId,
    Guid OperationId,
    int? ProgressPercent,
    string CurrentStep,
    DateTimeOffset OccurredAtUtc);
