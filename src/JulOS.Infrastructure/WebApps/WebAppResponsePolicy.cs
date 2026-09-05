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

    /// <summary>
    /// Rewrites an upstream <c>Set-Cookie</c> so the target application's cookie is accepted on the
    /// encoded proxy host: the <c>Domain</c> attribute is dropped (host-only), <c>Secure</c> is
    /// present only over HTTPS (so a plain-HTTP development deployment still receives the cookie),
    /// and every other attribute — <c>Path</c>, <c>SameSite</c>, <c>HttpOnly</c>, <c>Max-Age</c>,
    /// <c>Expires</c> — is preserved verbatim.
    /// </summary>
    public static string RewriteSetCookie(string value, bool requestIsHttps)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Set-Cookie separates attributes with ';'; a comma only appears inside an Expires value,
        // so splitting on ';' is safe and keeps the name=value pair (which may contain '=') intact.
        var segments = value.Split(';');
        var kept = new List<string> { segments[0].Trim() };
        for (var index = 1; index < segments.Length; index++)
        {
            var attribute = segments[index].Trim();
            if (attribute.Length == 0)
            {
                continue;
            }

            var name = AttributeName(attribute);
            if (name.Equals("domain", StringComparison.OrdinalIgnoreCase)
                || name.Equals("secure", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(attribute);
        }

        if (requestIsHttps)
        {
            kept.Add("Secure");
        }

        return string.Join("; ", kept);
    }

    /// <summary>
    /// Rewrites a redirect target header (<c>Location</c> or <c>Content-Location</c>) so navigation
    /// stays inside the JulOS proxy. Same-origin redirects stay on the current proxy host. When a
    /// dynamic proxy zone is available, cross-origin HTTP/HTTPS redirects are encoded into a new
    /// JulOS proxy host instead of escaping to the original Internet origin.
    /// </summary>
    public static string RewriteRedirect(
        string value,
        Uri upstream,
        Uri upstreamRequestUri,
        string requestScheme,
        string requestHost,
        string? dynamicProxyZone = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(upstreamRequestUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHost);

        Uri? target = null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            target = absolute;
        }
        else if (Uri.TryCreate(upstreamRequestUri, value, out var resolved)
            && resolved.Scheme is "http" or "https")
        {
            target = resolved;
        }

        if (target is null)
        {
            return value;
        }

        if (string.Equals(
                target.GetLeftPart(UriPartial.Authority),
                upstream.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(requestScheme, "://", requestHost, target.PathAndQuery, target.Fragment);
        }

        if (string.IsNullOrWhiteSpace(dynamicProxyZone))
        {
            return value;
        }

        var redirectedOrigin = new Uri(target.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
        var encodedHost = WebAppOriginCodec.EncodeHost(redirectedOrigin, dynamicProxyZone);
        return encodedHost is null
            ? value
            : string.Concat(requestScheme, "://", encodedHost, target.PathAndQuery, target.Fragment);
    }

    private static string AttributeName(string attribute)
    {
        var separator = attribute.IndexOf('=', StringComparison.Ordinal);
        return separator >= 0 ? attribute[..separator].Trim() : attribute;
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
