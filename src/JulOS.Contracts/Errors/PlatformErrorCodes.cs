namespace JulOS.Contracts.Errors;

/// <summary>
/// The error codes the platform itself produces.
/// </summary>
/// <remarks>
/// A feature declares its own codes next to the rule that raises them. Only failures
/// that no feature owns belong here, so this list stays short.
/// </remarks>
public static class PlatformErrorCodes
{
    /// <summary>The request did not match any route.</summary>
    public const string NotFound = "request.not_found";

    /// <summary>The request was rejected before any rule ran, for example by model validation.</summary>
    public const string Invalid = "request.invalid";

    /// <summary>The caller is not authenticated.</summary>
    public const string Unauthenticated = "request.unauthenticated";

    /// <summary>The caller is authenticated but not permitted.</summary>
    public const string Forbidden = "request.forbidden";

    /// <summary>A domain rule refused the operation and supplied no more specific code.</summary>
    public const string RuleViolation = "request.rule_violation";

    /// <summary>The server failed in a way it does not recognise. The cause stays server-side.</summary>
    public const string Unexpected = "server.unexpected";
}
