using System.Net;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.WebApps;

/// <summary>How an internal web-application target is presented in a desktop window.</summary>
public enum WebAppRenderingMode
{
    /// <summary>Reverse-proxied and rendered locally in the user's browser (default).</summary>
    Local,

    /// <summary>Rendered by an isolated browser runtime and streamed as a display (D005 fallback).</summary>
    Streamed,

    /// <summary>Attempt local rendering and fall back to streamed on incompatibility.</summary>
    Auto,
}

/// <summary>One approved internal web-application target served at its own JulOS host.</summary>
/// <param name="Host">The JulOS-facing host, for example <c>unifi.os.juloc.de</c>.</param>
/// <param name="Upstream">The absolute internal upstream base URI the proxy forwards to.</param>
/// <param name="RenderingMode">The presentation mode for the target.</param>
public sealed record WebAppTarget(string Host, Uri Upstream, WebAppRenderingMode RenderingMode);

/// <summary>Resolves an incoming request host to a configured local web-application target.</summary>
/// <remarks>
/// Targets are matched by host, never by path prefix: single-page applications use absolute
/// root paths and root WebSocket endpoints and break under a shared prefix (see
/// <c>docs/WEB-APP-RENDERING.md</c> and decision D035).
/// </remarks>
public sealed class WebAppTargetRegistry
{
    private readonly IReadOnlyDictionary<string, WebAppTarget> targetsByHost;
    private readonly WebAppDynamicProxyPolicy dynamicPolicy;

    private WebAppTargetRegistry(
        IReadOnlyDictionary<string, WebAppTarget> targetsByHost,
        WebAppDynamicProxyPolicy dynamicPolicy)
    {
        this.targetsByHost = targetsByHost;
        this.dynamicPolicy = dynamicPolicy;
    }

    /// <summary>Gets the number of configured local-proxy targets.</summary>
    public int Count => this.targetsByHost.Count;

    /// <summary>Reads the configured web-application targets. Missing configuration yields an empty registry.</summary>
    public static WebAppTargetRegistry Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var targets = new Dictionary<string, WebAppTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuration.GetSection("WebApps:Targets").GetChildren())
        {
            var host = NormalizeHost(entry["Host"])
                ?? throw new InvalidOperationException(
                    "A WebApps:Targets entry must set Host to one bare DNS host without scheme, port or path.");
            var upstream = ParseUpstream(entry["Upstream"]);
            var mode = ParseMode(entry["RenderingMode"]);

            if (!targets.TryAdd(host, new WebAppTarget(host, upstream, mode)))
            {
                throw new InvalidOperationException(
                    $"WebApps:Targets contains more than one entry for host '{host}'.");
            }
        }

        return new WebAppTargetRegistry(targets, WebAppDynamicProxyPolicy.Read(configuration));
    }

    /// <summary>Resolves a request host to a target that is served through the local proxy.</summary>
    /// <remarks>Only <see cref="WebAppRenderingMode.Local"/> and <see cref="WebAppRenderingMode.Auto"/> targets proxy.</remarks>
    public bool TryResolve(string? requestHost, out WebAppTarget target)
    {
        target = null!;
        var host = NormalizeRequestHost(requestHost);
        if (host is null)
        {
            return false;
        }

        if (this.targetsByHost.TryGetValue(host, out var match))
        {
            if (match.RenderingMode == WebAppRenderingMode.Streamed)
            {
                return false;
            }

            target = match;
            return true;
        }

        if (this.dynamicPolicy.TryResolve(host, out var upstream))
        {
            target = new WebAppTarget(host, upstream, WebAppRenderingMode.Local);
            return true;
        }

        return false;
    }

    /// <summary>Lists the hosts of every locally proxied target, ordered for a stable presentation.</summary>
    public IReadOnlyList<string> ProxiedHosts() =>
        this.targetsByHost.Values
            .Where(target => target.RenderingMode != WebAppRenderingMode.Streamed)
            .Select(target => target.Host)
            .OrderBy(host => host, StringComparer.Ordinal)
            .ToList();

    private static string? NormalizeRequestHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var host = value.Trim();
        var port = host.IndexOf(':', StringComparison.Ordinal);
        if (port >= 0)
        {
            host = host[..port];
        }

        return NormalizeHost(host);
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var host = value.Trim();

        // A bare host only: reject an accidental scheme, port, path, or user information.
        if (host.Contains("://", StringComparison.Ordinal)
            || host.Contains('/', StringComparison.Ordinal)
            || host.Contains(':', StringComparison.Ordinal)
            || host.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        if (Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            return null;
        }

        return host.ToLowerInvariant();
    }

    private static Uri ParseUpstream(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var upstream)
            || upstream.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(upstream.Fragment))
        {
            throw new InvalidOperationException(
                "A WebApps:Targets entry must set Upstream to one absolute HTTP or HTTPS URI without a fragment.");
        }

        return upstream;
    }

    private static WebAppRenderingMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WebAppRenderingMode.Local;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "local" => WebAppRenderingMode.Local,
            "streamed" => WebAppRenderingMode.Streamed,
            "auto" => WebAppRenderingMode.Auto,
            _ => throw new InvalidOperationException(
                "A WebApps:Targets RenderingMode must be 'local', 'streamed' or 'auto'."),
        };
    }
}

/// <summary>
/// Resolves a dynamically-encoded proxy host (<c>wa&lt;base32&gt;.&lt;zone&gt;</c>) to its target
/// origin when dynamic mode is enabled, gated by a default-deny SSRF allowlist of CIDR ranges and
/// DNS suffixes (see <c>docs/WEB-APP-RENDERING.md</c>).
/// </summary>
internal sealed class WebAppDynamicProxyPolicy
{
    private readonly bool enabled;
    private readonly string zone;
    private readonly IReadOnlyList<AllowEntry> allowlist;

    private WebAppDynamicProxyPolicy(bool enabled, string zone, IReadOnlyList<AllowEntry> allowlist)
    {
        this.enabled = enabled;
        this.zone = zone;
        this.allowlist = allowlist;
    }

    public static WebAppDynamicProxyPolicy Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue("WebApps:Dynamic:Enabled", false))
        {
            return new WebAppDynamicProxyPolicy(false, string.Empty, []);
        }

        var zone = configuration["WebApps:Dynamic:ProxyZone"]?.Trim().Trim('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(zone) || Uri.CheckHostName(zone) != UriHostNameType.Dns)
        {
            throw new InvalidOperationException(
                "WebApps:Dynamic:ProxyZone must be a DNS host when dynamic web-application mode is enabled.");
        }

        // The encoded host must sit under the session-cookie domain, otherwise the authenticated
        // JulOS session never reaches it and every proxied page fails to load.
        var cookieDomain = configuration["Authentication:CookieDomain"]?.Trim().Trim('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(cookieDomain)
            && zone != cookieDomain
            && !zone.EndsWith("." + cookieDomain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "WebApps:Dynamic:ProxyZone must be within Authentication:CookieDomain so the JulOS session reaches encoded proxy hosts.");
        }

        var allowlist = (configuration.GetSection("WebApps:Dynamic:AllowedHosts").Get<string[]>() ?? [])
            .Select(AllowEntry.TryParse)
            .OfType<AllowEntry>()
            .ToArray();

        return new WebAppDynamicProxyPolicy(true, zone, allowlist);
    }

    /// <summary>Decodes and authorizes a dynamic proxy host, or returns <see langword="false"/>.</summary>
    public bool TryResolve(string requestHost, out Uri upstream)
    {
        upstream = null!;
        if (!this.enabled
            || !WebAppOriginCodec.TryDecodeHost(requestHost, this.zone, out var decoded)
            || !this.IsAllowed(decoded))
        {
            return false;
        }

        upstream = decoded;
        return true;
    }

    private bool IsAllowed(Uri origin)
    {
        foreach (var entry in this.allowlist)
        {
            if (entry.Matches(origin.Host))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class AllowEntry
    {
        private readonly IPNetwork? network;
        private readonly string? suffix;

        private AllowEntry(IPNetwork? network, string? suffix)
        {
            this.network = network;
            this.suffix = suffix;
        }

        public static AllowEntry? TryParse(string? raw)
        {
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (value.Contains('/', StringComparison.Ordinal))
            {
                return IPNetwork.TryParse(value, out var network) ? new AllowEntry(network, null) : null;
            }

            return new AllowEntry(null, value.TrimStart('.').ToLowerInvariant());
        }

        public bool Matches(string host)
        {
            if (this.network is { } cidr)
            {
                var literal = host.StartsWith('[') && host.EndsWith(']')
                    ? host[1..^1]
                    : host;
                return IPAddress.TryParse(literal, out var address) && cidr.Contains(address);
            }

            var candidate = host.ToLowerInvariant();
            return candidate == this.suffix || candidate.EndsWith("." + this.suffix, StringComparison.Ordinal);
        }
    }
}
