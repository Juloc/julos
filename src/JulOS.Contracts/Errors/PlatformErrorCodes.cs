namespace JulOS.Contracts.Errors;

/// <summary>
/// Stable platform-owned error codes used by Problem Details responses.
/// </summary>
/// <remarks>
/// Package-owned codes use the <c>package.&lt;package-id&gt;.*</c> namespace and do
/// not belong in this type.
/// </remarks>
public static class PlatformErrorCodes
{
    /// <summary>An unhandled server failure whose details are available only in server logs.</summary>
    public const string InternalError = "platform.internal_error";

    /// <summary>The requested endpoint or resource does not exist.</summary>
    public const string NotFound = "request.not_found";

    /// <summary>The request is malformed or violates a transport-level requirement.</summary>
    public const string InvalidRequest = "request.invalid";

    /// <summary>A Domain invariant refused the requested operation.</summary>
    public const string DomainRuleViolation = "request.domain_rule_violation";

    /// <summary>A mutation used a stale resource revision and was not applied.</summary>
    public const string ConcurrencyConflict = "request.concurrency_conflict";

    /// <summary>The caller has not established an authenticated session.</summary>
    public const string AuthenticationRequired = "security.authentication_required";

    /// <summary>The authenticated caller is not permitted to perform the request.</summary>
    public const string PermissionDenied = "security.permission_denied";

    /// <summary>The request rate exceeded a configured limit.</summary>
    public const string RateLimitExceeded = "request.rate_limit_exceeded";
}
