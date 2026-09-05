using JulOS.Infrastructure.WebApps;

namespace JulOS.Infrastructure.Tests.WebApps;

[TestClass]
public sealed class WebAppContentRewriterTests
{
    [TestMethod]
    public void HtmlRewritesAbsoluteNavigationAndResourceUrlsIntoProxyHosts()
    {
        var rewritten = WebAppContentRewriter.RewriteHtml(
            "<html><head></head><body><a target=\"_blank\" href=\"https://example.com/path?q=1\">x</a><img src=\"//cdn.example.com/a.png\"></body></html>",
            new Uri("https://www.test.de/index.html"),
            "https",
            "os.juloc.de",
            _ => "signed-token");

        var exampleHost = WebAppOriginCodec.EncodeHost(new Uri("https://example.com/"), "os.juloc.de");
        var cdnHost = WebAppOriginCodec.EncodeHost(new Uri("https://cdn.example.com/"), "os.juloc.de");
        StringAssert.Contains(rewritten.Content, $"https://{exampleHost}/path?q=1");
        StringAssert.Contains(
            rewritten.Content,
            $"https://{cdnHost}/a.png?{WebAppContentRewriter.ProxyAccessTokenQueryParameter}=signed-token");
        StringAssert.Contains(rewritten.Content, "data-julos-browser-bridge");
        StringAssert.StartsWith(rewritten.ScriptHash, "sha256-");
    }

    [TestMethod]
    public void CssRewritesAbsoluteFontAndImportUrls()
    {
        var rewritten = WebAppContentRewriter.RewriteCss(
            "@import \"https://cdn.example.com/base.css\"; @font-face{src:url(https://cdn.example.com/f.woff2)}",
            new Uri("https://www.test.de/static/main.css"),
            "https",
            "os.juloc.de",
            _ => "signed-token");
        var cdnHost = WebAppOriginCodec.EncodeHost(new Uri("https://cdn.example.com/"), "os.juloc.de");
        StringAssert.Contains(
            rewritten,
            $"https://{cdnHost}/base.css?{WebAppContentRewriter.ProxyAccessTokenQueryParameter}=signed-token");
        StringAssert.Contains(
            rewritten,
            $"https://{cdnHost}/f.woff2?{WebAppContentRewriter.ProxyAccessTokenQueryParameter}=signed-token");
    }
}
