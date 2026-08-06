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

/// <summary>Creates, observes and advances durable operation resources.</summary>
public interface IOperationService
{
    /// <summary>Creates one queued operation or returns the matching idempotent result.</summary>
    Task<OperationSnapshot> CreateAsync(
        CreateOperationCommand command,
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
