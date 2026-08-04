using JulOS.Contracts.Remote;

namespace JulOS.Application.Remote;

/// <summary>Applies one trusted provider connection result to a connecting session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="RuntimeId">Exact provider runtime identity assigned during provisioning.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
public sealed record ConnectRemoteSessionCommand(
    Guid SessionId,
    string RuntimeId,
    long ExpectedRevision);

/// <summary>Applies one trusted caller-safe provider failure to a connecting or connected session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="RuntimeId">Exact provider runtime identity assigned during provisioning.</param>
/// <param name="ExpectedRevision">Optimistic concurrency revision.</param>
/// <param name="Code">Stable caller-safe failure code.</param>
/// <param name="Detail">Bounded caller-safe detail without provider exception text.</param>
/// <param name="Retryable">Whether a new session request may succeed without configuration changes.</param>
public sealed record FailRemoteSessionCommand(
    Guid SessionId,
    string RuntimeId,
    long ExpectedRevision,
    string Code,
    string Detail,
    bool Retryable);

/// <summary>Records trusted provider activity for an active Remote session.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="RuntimeId">Exact provider runtime identity assigned during provisioning.</param>
public sealed record RecordRemoteSessionActivityCommand(
    Guid SessionId,
    string RuntimeId);

/// <summary>Mutates provider-owned connection state without exposing persistence to an adapter.</summary>
public interface IRemoteSessionConnectionService
{
    /// <summary>Transitions an exactly matched provider runtime from connecting to connected.</summary>
    Task<RemoteSessionResponse> ConnectAsync(
        ConnectRemoteSessionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Transitions an exactly matched active provider runtime to a caller-safe failed state.</summary>
    Task<RemoteSessionResponse> FailAsync(
        FailRemoteSessionCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Refreshes activity for an exactly matched connected provider runtime.</summary>
    Task RecordActivityAsync(
        RecordRemoteSessionActivityCommand command,
        CancellationToken cancellationToken = default);
}
