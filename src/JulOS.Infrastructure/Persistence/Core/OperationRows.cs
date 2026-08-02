using JulOS.Application.Operations;

namespace JulOS.Infrastructure.Persistence.Core;

internal sealed class OperationRow
{
    internal Guid Id { get; set; }

    internal Guid OwnerUserId { get; set; }

    internal required string OperationType { get; set; }

    internal string? SourcePackageId { get; set; }

    internal required string TargetReference { get; set; }

    internal required string IdempotencyKey { get; set; }

    internal required string RequestFingerprint { get; set; }

    internal OperationState State { get; set; }

    internal int? ProgressPercent { get; set; }

    internal string? CurrentStep { get; set; }

    internal DateTimeOffset CreatedAtUtc { get; set; }

    internal DateTimeOffset? StartedAtUtc { get; set; }

    internal DateTimeOffset? CompletedAtUtc { get; set; }

    internal string? FailureCode { get; set; }

    internal string? FailureDetail { get; set; }

    internal required string CorrelationId { get; set; }

    internal DateTimeOffset? CancellationRequestedAtUtc { get; set; }

    internal int Revision { get; set; }

    internal List<OperationProgressEventRow> ProgressEvents { get; } = [];
}

internal sealed class OperationProgressEventRow
{
    internal Guid Id { get; set; }

    internal Guid OperationId { get; set; }

    internal int? ProgressPercent { get; set; }

    internal required string CurrentStep { get; set; }

    internal DateTimeOffset OccurredAtUtc { get; set; }
}
