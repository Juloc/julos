using JulOS.Contracts.Operations;

namespace JulOS.Application.Operations;

/// <summary>Reasons the operation-resource framework can refuse a request.</summary>
public enum OperationFailureReason
{
    /// <summary>The submitted representation is invalid.</summary>
    Invalid = 1,

    /// <summary>The requested resource does not exist for the caller.</summary>
    NotFound = 2,

    /// <summary>The idempotency key belongs to a different request.</summary>
    IdempotencyConflict = 3,

    /// <summary>The requested lifecycle transition is invalid.</summary>
    InvalidTransition = 4,

    /// <summary>The operation can no longer be cancelled.</summary>
    NotCancellable = 5,
}

/// <summary>A safe, typed refusal from operation management.</summary>
public sealed class OperationFailureException : Exception
{
    /// <summary>Creates one operation refusal.</summary>
    public OperationFailureException(OperationFailureReason reason)
        : base(MessageFor(reason))
    {
        this.Reason = reason;
    }

    /// <summary>Creates one operation refusal while retaining its internal cause.</summary>
    public OperationFailureException(OperationFailureReason reason, Exception innerException)
        : base(MessageFor(reason), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>The stable refusal reason.</summary>
    public OperationFailureReason Reason { get; }

    /// <summary>The public machine-readable code.</summary>
    public string Code => this.Reason switch
    {
        OperationFailureReason.Invalid => OperationErrorCodes.Invalid,
        OperationFailureReason.NotFound => OperationErrorCodes.NotFound,
        OperationFailureReason.IdempotencyConflict => OperationErrorCodes.IdempotencyConflict,
        OperationFailureReason.InvalidTransition => OperationErrorCodes.InvalidTransition,
        OperationFailureReason.NotCancellable => OperationErrorCodes.NotCancellable,
        _ => throw new InvalidOperationException("Unknown operation failure."),
    };

    private static string MessageFor(OperationFailureReason reason) => reason switch
    {
        OperationFailureReason.Invalid => "The operation representation is invalid.",
        OperationFailureReason.NotFound => "The operation does not exist.",
        OperationFailureReason.IdempotencyConflict => "The idempotency key belongs to a different operation request.",
        OperationFailureReason.InvalidTransition => "The operation cannot make the requested lifecycle transition.",
        OperationFailureReason.NotCancellable => "The operation can no longer be cancelled.",
        _ => "Operation management failed.",
    };
}
