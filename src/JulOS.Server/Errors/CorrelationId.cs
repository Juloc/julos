namespace JulOS.Server.Errors;

/// <summary>
/// The identifier that ties one request to every log entry and error response it produced.
/// </summary>
internal static class CorrelationId
{
    /// <summary>The request and response header carrying the identifier.</summary>
    internal const string HeaderName = "X-Correlation-Id";

    private const int MaximumLength = 64;

    private static readonly object ItemKey = new();

    /// <summary>
    /// Returns a supplied identifier when it is safe to echo, and a new one otherwise.
    /// </summary>
    /// <remarks>
    /// A caller-supplied value is written into logs and into a response header, so it is
    /// accepted only as a short run of unreserved characters. Anything else could carry a
    /// line break into a log file or a control character into a header.
    /// </remarks>
    internal static string Accept(string? supplied)
    {
        return IsSafe(supplied) ? supplied! : Guid.CreateVersion7().ToString("D");
    }

    /// <summary>Stores the identifier for the rest of the request.</summary>
    internal static void Set(HttpContext context, string correlationId)
    {
        context.Items[ItemKey] = correlationId;
    }

    /// <summary>
    /// Returns the identifier of the current request.
    /// </summary>
    /// <remarks>
    /// Falls back to the framework trace identifier. An error response without any
    /// correlation identifier would be undiagnosable, which is worse than an identifier
    /// that did not come from the middleware.
    /// </remarks>
    internal static string Get(HttpContext context)
    {
        return context.Items.TryGetValue(ItemKey, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
    }

    private static bool IsSafe(string? supplied)
    {
        if (string.IsNullOrEmpty(supplied) || supplied.Length > MaximumLength)
        {
            return false;
        }

        foreach (var character in supplied)
        {
            var allowed = char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
