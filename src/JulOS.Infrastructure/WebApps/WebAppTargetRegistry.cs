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

    private WebAppTargetRegistry(IReadOnlyDictionary<string, WebAppTarget> targetsByHost)
    {
        this.targetsByHost = targetsByHost;
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

        return new WebAppTargetRegistry(targets);
    }

    /// <summary>Resolves a request host to a target that is served through the local proxy.</summary>
    /// <remarks>Only <see cref="WebAppRenderingMode.Local"/> and <see cref="WebAppRenderingMode.Auto"/> targets proxy.</remarks>
    public bool TryResolve(string? requestHost, out WebAppTarget target)
    {
        target = null!;
        var host = NormalizeRequestHost(requestHost);
        if (host is null || !this.targetsByHost.TryGetValue(host, out var match))
        {
            return false;
        }

        if (match.RenderingMode == WebAppRenderingMode.Streamed)
        {
            return false;
        }

        target = match;
        return true;
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
