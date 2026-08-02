namespace JulOS.Contracts.Errors;

/// <summary>
/// Stable members added to every JulOS Problem Details response.
/// </summary>
/// <remarks>
/// These names are part of the versioned public API. Do not rename them without
/// a contract version change.
/// </remarks>
public static class ProblemExtensionNames
{
    /// <summary>The stable machine-readable platform error code.</summary>
    public const string Code = "code";

    /// <summary>The request correlation identifier also emitted in the response header.</summary>
    public const string CorrelationId = "correlationId";

    /// <summary>Whether retrying the identical request may succeed without user action.</summary>
    public const string Retryable = "retryable";

    /// <summary>An optional server-directed delay before retrying.</summary>
    public const string RetryAfterSeconds = "retryAfterSeconds";

    /// <summary>The authoritative revision returned for an optimistic-concurrency conflict.</summary>
    public const string CurrentRevision = "currentRevision";
}
