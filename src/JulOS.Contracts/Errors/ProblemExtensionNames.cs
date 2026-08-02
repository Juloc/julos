namespace JulOS.Contracts.Errors;

/// <summary>
/// The names of the members JulOS adds to a Problem Details response.
/// </summary>
/// <remarks>
/// These names are part of the public contract. A client reads them to decide whether
/// to retry, which field to highlight and which correlation identifier to quote in a
/// support request, so renaming one is a breaking change.
/// </remarks>
public static class ProblemExtensionNames
{
    /// <summary>The stable machine-readable error code, for example <c>package.worker_not_ready</c>.</summary>
    public const string Code = "code";

    /// <summary>The identifier that ties this response to the server-side log entries of the same request.</summary>
    public const string CorrelationId = "correlationId";

    /// <summary>Whether repeating the identical request can reasonably succeed later.</summary>
    public const string Retryable = "retryable";

    /// <summary>The package that produced the failure, when a package produced it.</summary>
    public const string SourcePackage = "sourcePackage";

    /// <summary>Per-field validation messages, keyed by the request field name.</summary>
    public const string FieldErrors = "fieldErrors";

    /// <summary>The revision the server currently holds, returned with a concurrency conflict.</summary>
    public const string CurrentRevision = "currentRevision";
}
