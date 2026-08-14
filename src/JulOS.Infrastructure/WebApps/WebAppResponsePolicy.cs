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
    public static string? RewriteContentSecurityPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var kept = new List<string>();
        foreach (var directive in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IsFrameAncestors(directive))
            {
                continue;
            }

            kept.Add(directive);
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
