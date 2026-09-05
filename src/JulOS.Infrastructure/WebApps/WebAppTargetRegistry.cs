using System.Net;

using Microsoft.Extensions.Configuration;

namespace JulOS.Infrastructure.WebApps;

/// <summary>One approved internal web-application target served at its own JulOS host.</summary>
/// <param name="Host">The JulOS-facing host, for example <c>unifi.os.juloc.de</c>.</param>
/// <param name="Upstream">The absolute internal upstream base URI the proxy forwards to.</param>
/// <param name="RequiresAddressPinning">
/// Whether this target came from the dynamic URL proxy and therefore requires DNS resolution to an
/// explicitly allowed address before a connection is opened.
/// </param>
public sealed record WebAppTarget(
    string Host,
    Uri Upstream,
    bool RequiresAddressPinning = false);

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

    /// <summary>Gets whether the dynamic "type a URL" proxy mode is enabled.</summary>
    public bool DynamicEnabled => this.dynamicPolicy.Enabled;

    /// <summary>Gets the DNS zone under which encoded dynamic proxy hosts are served.</summary>
    public string DynamicProxyZone => this.dynamicPolicy.ProxyZone;

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

            if (!targets.TryAdd(host, new WebAppTarget(host, upstream)))
            {
                throw new InvalidOperationException(
                    $"WebApps:Targets contains more than one entry for host '{host}'.");
            }
        }

        return new WebAppTargetRegistry(targets, WebAppDynamicProxyPolicy.Read(configuration));
    }

    /// <summary>Resolves a request host to a target that is served through the local proxy.</summary>
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
            target = match;
            return true;
        }

        if (this.dynamicPolicy.TryResolve(host, out var upstream))
        {
            target = new WebAppTarget(host, upstream, RequiresAddressPinning: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a dynamic target to the exact IP addresses it is allowed to connect to. Static
    /// administrator-configured targets return an empty array because they do not use this policy.
    /// </summary>
    public Task<IPAddress[]> ResolveAllowedAddressesAsync(
        WebAppTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.RequiresAddressPinning
            ? this.dynamicPolicy.ResolveAllowedAddressesAsync(target.Upstream, cancellationToken)
            : Task.FromResult(Array.Empty<IPAddress>());
    }

    /// <summary>Lists the hosts of every locally proxied target, ordered for a stable presentation.</summary>
    public IReadOnlyList<string> ProxiedHosts() =>
        this.targetsByHost.Values
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

}

/// <summary>
/// Resolves a dynamically-encoded proxy host (<c>wa&lt;base32&gt;.&lt;zone&gt;</c>) to its target
/// origin when dynamic mode is enabled. Public Internet destinations are permitted through pinned
/// DNS resolution, while non-public address space remains default-deny unless explicitly allowlisted
/// by CIDR/DNS policy (see <c>docs/WEB-APP-RENDERING.md</c>).
/// </summary>
internal sealed class WebAppDynamicProxyPolicy
{
    private readonly bool enabled;
    private readonly string zone;
    private readonly bool allowPublicInternet;
    private readonly IReadOnlyList<AllowEntry> allowlist;

    private WebAppDynamicProxyPolicy(
        bool enabled,
        string zone,
        bool allowPublicInternet,
        IReadOnlyList<AllowEntry> allowlist)
    {
        this.enabled = enabled;
        this.zone = zone;
        this.allowPublicInternet = allowPublicInternet;
        this.allowlist = allowlist;
    }

    public bool Enabled => this.enabled;

    public string ProxyZone => this.zone;

    public static WebAppDynamicProxyPolicy Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue("WebApps:Dynamic:Enabled", false))
        {
            return new WebAppDynamicProxyPolicy(false, string.Empty, false, []);
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

        var allowPublicInternet = configuration.GetValue("WebApps:Dynamic:AllowPublicInternet", true);
        var allowlist = (configuration.GetSection("WebApps:Dynamic:AllowedHosts").Get<string[]>() ?? [])
            .Select(AllowEntry.TryParse)
            .OfType<AllowEntry>()
            .ToArray();

        return new WebAppDynamicProxyPolicy(true, zone, allowPublicInternet, allowlist);
    }

    /// <summary>Decodes and authorizes a dynamic proxy host, or returns <see langword="false"/>.</summary>
    public bool TryResolve(string requestHost, out Uri upstream)
    {
        upstream = null!;
        if (!this.enabled
            || !WebAppOriginCodec.TryDecodeHost(requestHost, this.zone, out var decoded)
            || !this.IsOriginHostAllowed(decoded))
        {
            return false;
        }

        upstream = decoded;
        return true;
    }

    /// <summary>
    /// Resolves the target once and retains only addresses allowed by policy. Public Internet
    /// addresses are allowed when enabled; non-public addresses require an explicit CIDR allowlist.
    /// DNS is resolved once and the selected connection is pinned to that validated address set.
    /// </summary>
    public async Task<IPAddress[]> ResolveAllowedAddressesAsync(Uri origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var literalHost = NormalizeAddressLiteral(origin.Host);
        if (IPAddress.TryParse(literalHost, out var literalAddress))
        {
            return this.AddressIsAllowed(literalAddress) ? [literalAddress] : [];
        }

        var resolved = await Dns.GetHostAddressesAsync(origin.IdnHost, cancellationToken).ConfigureAwait(false);
        return resolved
            .Where(this.AddressIsAllowed)
            .Distinct()
            .ToArray();
    }

    private bool IsOriginHostAllowed(Uri origin)
    {
        var literalHost = NormalizeAddressLiteral(origin.Host);
        if (IPAddress.TryParse(literalHost, out var literalAddress))
        {
            return this.AddressIsAllowed(literalAddress);
        }

        var dnsName = origin.IdnHost.ToLowerInvariant();
        return this.allowPublicInternet || this.allowlist.Any(entry => entry.MatchesDnsName(dnsName));
    }

    private bool AddressIsAllowed(IPAddress address) =>
        this.allowlist.Any(entry => entry.MatchesAddress(address))
        || (this.allowPublicInternet && IsPublicInternetAddress(address));

    private static bool IsPublicInternetAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];

            return first != 0
                && first != 10
                && first != 127
                && !(first == 100 && second >= 64 && second <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second >= 16 && second <= 31)
                && !(first == 192 && second == 0 && third == 0)
                && !(first == 192 && second == 0 && third == 2)
                && !(first == 192 && second == 168)
                && !(first == 198 && (second == 18 || second == 19))
                && !(first == 198 && second == 51 && third == 100)
                && !(first == 203 && second == 0 && third == 113)
                && first < 224;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6
            || IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        if ((ipv6[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        // 2001:db8::/32 is documentation-only and must not be treated as a public destination.
        return !(ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8);
    }

    private static string NormalizeAddressLiteral(string host) =>
        host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;

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

        public bool MatchesDnsName(string host) =>
            this.suffix is not null
            && (host == this.suffix || host.EndsWith("." + this.suffix, StringComparison.Ordinal));

        public bool MatchesAddress(IPAddress address) =>
            this.network is { } network && network.Contains(address);
    }
}
