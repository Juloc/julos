namespace JulOS.Infrastructure.WebApps;

/// <summary>
/// Pure response-header policy for the local web-application proxy: it removes framing
/// restrictions so the JulOS shell can embed the target, and drops hop-by-hop headers that
/// must not be forwarded across the proxy boundary (see <c>docs/WEB-APP-RENDERING.md</c>).
/// </summary>
public static class WebAppResponsePolicy
{
    /// <summary>The <c>frame-ancestors</c> directive name, matched case-insensitively.</summary>
    private const string FrameAncestorsDirective = "frame-ancestors";

    /// <summary>The name prefix of every JulOS-owned cookie, which must never reach an upstream.</summary>
    private const string JulOsCookiePrefix = ".JulOS.";

    // Hop-by-hop headers (RFC 9110) plus X-Frame-Options. The proxy re-frames the response,
    // so transfer framing and connection headers must not be copied from the upstream, and
    // X-Frame-Options is removed so the JulOS shell origin can embed the target in an iframe.
    private static readonly HashSet<string> SuppressedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "X-Frame-Options",
    };

    /// <summary>Gets whether an upstream response header must not be copied to the client.</summary>
    public static bool IsSuppressedResponseHeader(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return SuppressedResponseHeaders.Contains(name);
    }

    /// <summary>
    /// Removes the <c>frame-ancestors</c> directive from a Content-Security-Policy value so the
    /// policy no longer forbids embedding. Every other directive is preserved verbatim.
    /// </summary>
    /// <returns>The rewritten policy, or <see langword="null"/> when nothing remains.</returns>
    /// <remarks>
    /// A Content-Security-Policy header may carry several policies separated by top-level commas
    /// (equivalently, an upstream may send the header more than once). A top-level comma cannot
    /// appear inside a directive value, so each comma-separated policy is rewritten independently.
    /// </remarks>
    public static string? RewriteContentSecurityPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var policies = new List<string>();
        foreach (var policy in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kept = new List<string>();
            foreach (var directive in policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsFrameAncestors(directive))
                {
                    continue;
                }

                kept.Add(directive);
            }

            if (kept.Count > 0)
            {
                policies.Add(string.Join("; ", kept));
            }
        }

        return policies.Count == 0 ? null : string.Join(", ", policies);
    }

    /// <summary>
    /// Removes JulOS's own cookies (those named with the <c>.JulOS.</c> prefix) from an outbound
    /// <c>Cookie</c> header so the internal upstream never receives the user's JulOS session or
    /// antiforgery credentials. The target application's own cookies are preserved.
    /// </summary>
    /// <returns>The filtered cookie header, or <see langword="null"/> when nothing remains to send.</returns>
    public static string? FilterForwardedCookies(string? cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return null;
        }

        var kept = new List<string>();
        foreach (var pair in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = separator >= 0 ? pair[..separator] : pair;
            if (name.StartsWith(JulOsCookiePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(pair);
        }

        return kept.Count == 0 ? null : string.Join("; ", kept);
    }

    private static bool IsFrameAncestors(string directive)
    {
        var name = directive.AsSpan();
        var space = name.IndexOf(' ');
        if (space >= 0)
        {
            name = name[..space];
        }

        return name.Equals(FrameAncestorsDirective, StringComparison.OrdinalIgnoreCase);
    }
}
