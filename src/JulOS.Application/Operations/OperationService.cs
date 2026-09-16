namespace JulOS.Application.Operations;

/// <summary>Lifecycle states stored for durable background operations.</summary>
public enum OperationState
{
    /// <summary>The operation is waiting for its owning executor.</summary>
    Queued = 1,

    /// <summary>The owning executor is performing the work.</summary>
    Running = 2,

    /// <summary>The requested state was verified.</summary>
    Succeeded = 3,

    /// <summary>The operation ended with a safe failure cause.</summary>
    Failed = 4,

    /// <summary>The operation ended after cancellation.</summary>
    Cancelled = 5,
}

/// <summary>Input for creating one idempotent operation resource.</summary>
public sealed record CreateOperationCommand(
    Guid OwnerUserId,
    string OperationType,
    string? SourcePackageId,
    string TargetReference,
    string IdempotencyKey,
    string CorrelationId);

/// <summary>The persistence-independent current state of one operation.</summary>
public sealed record OperationSnapshot(
    Guid OperationId,
    string OperationType,
    Guid OwnerUserId,
    string? SourcePackageId,
    string TargetReference,
    OperationState State,
    int? ProgressPercent,
    string? CurrentStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode,
    string? FailureDetail,
    string CorrelationId,
    DateTimeOffset? CancellationRequestedAtUtc,
    int Revision);

/// <summary>The persistence-independent representation of one progress event.</summary>
public sealed record OperationProgressSnapshot(
    Guid EventId,
    Guid OperationId,
    int? ProgressPercent,
    string CurrentStep,
    DateTimeOffset OccurredAtUtc);

/// <summary>Which operations one page of the Operation Center asks for.</summary>
/// <param name="OwnerUserId">Authenticated user; the list is always filtered to them.</param>
/// <param name="States">Optional subset of the public states.</param>
/// <param name="SourcePackageId">Optional originating package.</param>
/// <param name="CreatedAfterUtc">Optional lower bound on creation time.</param>
/// <param name="Cursor">Opaque continuation from a previous page.</param>
/// <param name="Limit">Page size; defaults to 50 and is capped at 200.</param>
public sealed record OperationQuery(
    Guid OwnerUserId,
    IReadOnlyList<OperationState>? States = null,
    string? SourcePackageId = null,
    DateTimeOffset? CreatedAfterUtc = null,
    string? Cursor = null,
    int? Limit = null);

/// <summary>One page of operations and the cursor that continues it.</summary>
/// <param name="Items">Operations, newest first.</param>
/// <param name="NextCursor">Opaque continuation, or null when the page is the last.</param>
public sealed record OperationPage(
    IReadOnlyList<OperationSnapshot> Items,
    string? NextCursor);

/// <summary>Creates, observes and advances durable operation resources.</summary>
public interface IOperationService
{
    /// <summary>Creates one queued operation or returns the matching idempotent result.</summary>
    Task<OperationSnapshot> CreateAsync(
        CreateOperationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Lists one page of the authenticated user's operations, newest first.</summary>
    /// <remarks>
    /// Always filtered to the owner. A global read permission does not silently turn this
    /// into a cross-user administrative API: the Operation Center shows a user their own
    /// work and nothing else.
    /// </remarks>
    Task<OperationPage> ListAsync(
        OperationQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one operation owned by the supplied user.</summary>
    Task<OperationSnapshot> ReadAsync(
        Guid operationId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the immutable progress stream of one operation owned by the supplied user.</summary>
    Task<IReadOnlyList<OperationProgressSnapshot>> ReadProgressAsync(
        Guid operationId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels queued work or persists a cancellation request for running work.</summary>
    Task<OperationSnapshot> RequestCancellationAsync(
        Guid operationId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks queued work as started by its owning executor.</summary>
    Task<OperationSnapshot> MarkRunningAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    /// <summary>Persists one progress event and updates the current operation summary atomically.</summary>
    Task<OperationSnapshot> ReportProgressAsync(
        Guid operationId,
        int? progressPercent,
        string currentStep,
        CancellationToken cancellationToken = default);

    /// <summary>Marks running work as successful only after its target state was verified.</summary>
    Task<OperationSnapshot> MarkSucceededAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks running work as failed with a stable code and sanitized safe cause.</summary>
    Task<OperationSnapshot> MarkFailedAsync(
        Guid operationId,
        string failureCode,
        string safeFailureDetail,
        CancellationToken cancellationToken = default);

    /// <summary>Marks queued or running work as cancelled by its owning executor.</summary>
    Task<OperationSnapshot> MarkCancelledAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);
}
