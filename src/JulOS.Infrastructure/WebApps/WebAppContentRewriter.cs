using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JulOS.Infrastructure.WebApps;

/// <summary>
/// Rewrites browser-visible HTML/CSS content so absolute HTTP(S) references remain inside the
/// encoded JulOS proxy namespace. HTML also receives the Browser bridge used for URL synchronization
/// and proxied popup/new-tab navigation.
/// </summary>
public static partial class WebAppContentRewriter
{
    private const int MaximumProxyLabelLength = 63;

    /// <summary>Rewritten HTML content plus the CSP source hash for the injected Browser bridge.</summary>
    public sealed class RewrittenHtml
    {
        /// <summary>Creates a rewritten HTML result.</summary>
        public RewrittenHtml(string content, string scriptHash)
        {
            this.Content = content;
            this.ScriptHash = scriptHash;
        }

        /// <summary>Gets the rewritten HTML document.</summary>
        public string Content { get; }

        /// <summary>Gets the SHA-256 CSP source expression without surrounding quotes.</summary>
        public string ScriptHash { get; }
    }

    /// <summary>Rewrites absolute HTML/CSS references and injects the Browser bridge.</summary>
    public static RewrittenHtml RewriteHtml(
        string html,
        Uri upstreamRequestUri,
        string requestScheme,
        string proxyZone)
    {
        ArgumentNullException.ThrowIfNull(html);
        var rewritten = HtmlAbsoluteAttributeRegex().Replace(
            html,
            match => string.Concat(
                match.Groups["prefix"].Value,
                RewriteUrl(match.Groups["url"].Value, upstreamRequestUri, requestScheme, proxyZone),
                match.Groups["suffix"].Value));
        rewritten = RewriteCss(rewritten, upstreamRequestUri, requestScheme, proxyZone);

        var bridge = BuildBrowserBridge(upstreamRequestUri, requestScheme, proxyZone);
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(bridge)));
        var script = $"<script data-julos-browser-bridge=\"1\">{bridge}</script>";
        var head = HeadOpenRegex().Match(rewritten);
        rewritten = head.Success
            ? rewritten.Insert(head.Index + head.Length, script)
            : script + rewritten;

        return new RewrittenHtml(rewritten, $"sha256-{hash}");
    }

    /// <summary>Rewrites absolute HTTP(S) URLs in CSS into JulOS proxy URLs.</summary>
    public static string RewriteCss(
        string css,
        Uri upstreamRequestUri,
        string requestScheme,
        string proxyZone)
    {
        ArgumentNullException.ThrowIfNull(css);
        var rewritten = CssUrlRegex().Replace(
            css,
            match => string.Concat(
                match.Groups["prefix"].Value,
                RewriteUrl(match.Groups["url"].Value, upstreamRequestUri, requestScheme, proxyZone),
                match.Groups["suffix"].Value));
        return CssImportRegex().Replace(
            rewritten,
            match => string.Concat(
                match.Groups["prefix"].Value,
                RewriteUrl(match.Groups["url"].Value, upstreamRequestUri, requestScheme, proxyZone),
                match.Groups["suffix"].Value));
    }

    /// <summary>Maps one absolute or scheme-relative HTTP(S) URL into the encoded proxy zone.</summary>
    public static string RewriteUrl(
        string value,
        Uri upstreamRequestUri,
        string requestScheme,
        string proxyZone)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(upstreamRequestUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestScheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(proxyZone);

        if (!Uri.TryCreate(upstreamRequestUri, value, out var target)
            || target.Scheme is not ("http" or "https"))
        {
            return value;
        }

        if (WebAppOriginCodec.TryDecodeHost(target.Host, proxyZone, out _))
        {
            return value;
        }

        var origin = new Uri(target.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute);
        var encodedHost = WebAppOriginCodec.EncodeHost(origin, proxyZone);
        return encodedHost is null
            ? value
            : string.Concat(requestScheme, "://", encodedHost, target.PathAndQuery, target.Fragment);
    }

    private static string BuildBrowserBridge(Uri upstreamRequestUri, string requestScheme, string proxyZone)
    {
        var realOriginJson = JsonSerializer.Serialize(upstreamRequestUri.GetLeftPart(UriPartial.Authority));
        var finalPathJson = JsonSerializer.Serialize(string.Concat(
            upstreamRequestUri.AbsolutePath,
            upstreamRequestUri.Query,
            upstreamRequestUri.Fragment));
        var zoneJson = JsonSerializer.Serialize(proxyZone.Trim('.').ToLowerInvariant());
        var schemeJson = JsonSerializer.Serialize(requestScheme + ":");

        const string template = """
(()=>{const z=__ZONE__,ro=__REAL_ORIGIN__,fp=__FINAL_PATH__,ps=__SCHEME__,a="abcdefghijklmnopqrstuvwxyz234567";
const b=d=>{let o="",q=0,n=0;for(const v of d){q=(q<<8)|v;n+=8;while(n>=5){n-=5;o+=a[(q>>n)&31]}}if(n>0)o+=a[(q<<(5-n))&31];return o};
const p=v=>{if(typeof v!=="string")return v;let u;try{u=new URL(v,location.href)}catch{return v}if(u.protocol!=="http:"&&u.protocol!=="https:")return v;if(u.hostname===z||u.hostname.endsWith("."+z))return u.href;const s=u.protocol==="https:"?1:0,pt=u.port||(s?"443":"80"),e=new TextEncoder().encode(u.hostname.toLowerCase()+":"+pt),d=new Uint8Array(e.length+1);d[0]=s;d.set(e,1);const l="wa"+b(d);return l.length>__MAX_LABEL__?v:ps+"//"+l+"."+z+u.pathname+u.search+u.hash};
if(location.pathname+location.search!==fp)try{history.replaceState(history.state,"",fp)}catch{}
const tell=()=>{try{parent.postMessage({type:"julos-browser-location",url:ro+location.pathname+location.search+location.hash},"*")}catch{}};
tell();const wo=window.open;window.open=function(v,...r){return wo.call(this,p(v),...r)};
document.addEventListener("click",e=>{const t=e.target&&e.target.closest?e.target.closest('a[target="_blank"],a[target="_new"]'):null;if(t&&t.href)t.href=p(t.href)},true);
document.addEventListener("submit",e=>{const f=e.target;if(f&&f.target==="_blank"&&f.action)f.action=p(f.action)},true);
const hp=history.pushState.bind(history),hr=history.replaceState.bind(history);history.pushState=(...r)=>{hp(...r);queueMicrotask(tell)};history.replaceState=(...r)=>{hr(...r);queueMicrotask(tell)};addEventListener("popstate",tell);addEventListener("hashchange",tell)})();
""";

        return template
            .Replace("__ZONE__", zoneJson, StringComparison.Ordinal)
            .Replace("__REAL_ORIGIN__", realOriginJson, StringComparison.Ordinal)
            .Replace("__FINAL_PATH__", finalPathJson, StringComparison.Ordinal)
            .Replace("__SCHEME__", schemeJson, StringComparison.Ordinal)
            .Replace("__MAX_LABEL__", MaximumProxyLabelLength.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"(?<prefix>\b(?:href|src|action|formaction|poster)\s*=\s*(?<q>[""']))(?<url>(?:https?:)?//[^""'<>]+)(?<suffix>\k<q>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlAbsoluteAttributeRegex();

    [GeneratedRegex(
        @"(?<prefix>url\(\s*(?<q>[""']?))(?<url>(?:https?:)?//[^)""']+)(?<suffix>\k<q>\s*\))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssUrlRegex();

    [GeneratedRegex(
        @"(?<prefix>@import\s+(?<q>[""']))(?<url>(?:https?:)?//[^""']+)(?<suffix>\k<q>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssImportRegex();

    [GeneratedRegex(@"<head\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadOpenRegex();
}
