namespace JulOS.Contracts.Remote;

/// <summary>Stable contract names used by the private Remote provider event endpoint.</summary>
public static class RemoteProviderEventContract
{
    /// <summary>Header carrying one session- and runtime-scoped callback token.</summary>
    public const string TokenHeader = "X-JulOS-Remote-Token";

    /// <summary>Provider completed its connection handshake.</summary>
    public const string Connected = "connected";

    /// <summary>Provider observed a caller-safe connection failure.</summary>
    public const string Failed = "failed";

    /// <summary>Provider observed session activity.</summary>
    public const string Activity = "activity";
}

/// <summary>One authenticated event emitted by a Remote provider runtime.</summary>
/// <param name="SessionId">Stable session identity.</param>
/// <param name="RuntimeId">Exact Runtime Manager identity.</param>
/// <param name="Event">One value from <see cref="RemoteProviderEventContract"/>.</param>
/// <param name="ExpectedRevision">Required optimistic revision for state-changing events.</param>
/// <param name="FailureCode">Caller-safe failure code for a failed event.</param>
/// <param name="FailureDetail">Bounded caller-safe failure detail for a failed event.</param>
/// <param name="Retryable">Whether a new session may succeed without configuration changes.</param>
public sealed record RemoteProviderEventRequest(
    Guid SessionId,
    string RuntimeId,
    string Event,
    long ExpectedRevision,
    string? FailureCode,
    string? FailureDetail,
    bool Retryable);
