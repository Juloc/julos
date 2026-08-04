using JulOS.Contracts.Remote;

namespace JulOS.Application.Remote;

/// <summary>Creates one user- and package-owned Remote session.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="Request">Validated protocol-neutral request payload.</param>
public sealed record CreateRemoteSessionCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    CreateRemoteSessionRequest Request);

/// <summary>Reads one user- and package-owned Remote session.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="Request">Read request.</param>
public sealed record ReadRemoteSessionCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    ReadRemoteSessionRequest Request);

/// <summary>Lists one user's Remote sessions created by one caller package.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="Request">Bounded list request.</param>
public sealed record ListRemoteSessionsCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    ListRemoteSessionsRequest Request);

/// <summary>Cancels one user- and package-owned Remote session.</summary>
/// <param name="OwnerUserId">Authenticated owning user.</param>
/// <param name="CallerPackageId">Authorized caller package.</param>
/// <param name="Request">Cancellation request.</param>
public sealed record CancelRemoteSessionCommand(
    Guid OwnerUserId,
    string CallerPackageId,
    CancelRemoteSessionRequest Request);

/// <summary>Durable Remote session orchestration boundary before provider runtime allocation.</summary>
public interface IRemoteSessionService
{
    /// <summary>Creates or recovers one exact-idempotent session request.</summary>
    Task<RemoteSessionResponse> CreateAsync(
        CreateRemoteSessionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one session visible to the owning user and caller package.</summary>
    Task<RemoteSessionResponse> ReadAsync(
        ReadRemoteSessionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Lists one bounded page visible to the owning user and caller package.</summary>
    Task<RemoteSessionListResponse> ListAsync(
        ListRemoteSessionsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels one session with optimistic concurrency and cancellation idempotency.</summary>
    Task<RemoteSessionResponse> CancelAsync(
        CancelRemoteSessionCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable Remote session service failure reasons.</summary>
public enum RemoteSessionServiceFailureReason
{
    /// <summary>Caller ownership data is malformed.</summary>
    InvalidCaller = 0,

    /// <summary>The owned session does not exist.</summary>
    NotFound = 1,

    /// <summary>The create operation key was reused with a different request digest.</summary>
    IdempotencyConflict = 2,

    /// <summary>The supplied revision does not match durable state.</summary>
    ConcurrencyConflict = 3,

    /// <summary>The requested lifecycle change is not allowed.</summary>
    InvalidTransition = 4,

    /// <summary>The list cursor is invalid or belongs to a different query scope.</summary>
    CursorInvalid = 5,
}

/// <summary>Caller-safe failure raised by durable Remote session orchestration.</summary>
public sealed class RemoteSessionServiceException : Exception
{
    /// <summary>Creates a Remote session service failure.</summary>
    public RemoteSessionServiceException(
        RemoteSessionServiceFailureReason reason,
        Exception? innerException = null)
        : base(Message(reason), innerException)
    {
        this.Reason = reason;
    }

    /// <summary>Gets the stable failure reason.</summary>
    public RemoteSessionServiceFailureReason Reason { get; }

    private static string Message(RemoteSessionServiceFailureReason reason) => reason switch
    {
        RemoteSessionServiceFailureReason.InvalidCaller => "Remote session caller identity is invalid.",
        RemoteSessionServiceFailureReason.NotFound => "Remote session was not found.",
        RemoteSessionServiceFailureReason.IdempotencyConflict => "Remote operation key was reused with a different request.",
        RemoteSessionServiceFailureReason.ConcurrencyConflict => "Remote session revision is stale.",
        RemoteSessionServiceFailureReason.InvalidTransition => "Remote session transition is invalid.",
        RemoteSessionServiceFailureReason.CursorInvalid => "Remote session cursor is invalid.",
        _ => "Remote session operation failed.",
    };
}
