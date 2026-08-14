using JulOS.Infrastructure.WebApps;

namespace JulOS.Infrastructure.Tests.WebApps;

[TestClass]
public sealed class WebAppResponsePolicyTests
{
    [TestMethod]
    public void SuppressesFramingAndHopByHopHeadersCaseInsensitively()
    {
        Assert.IsTrue(WebAppResponsePolicy.IsSuppressedResponseHeader("X-Frame-Options"));
        Assert.IsTrue(WebAppResponsePolicy.IsSuppressedResponseHeader("x-frame-options"));
        Assert.IsTrue(WebAppResponsePolicy.IsSuppressedResponseHeader("Connection"));
        Assert.IsTrue(WebAppResponsePolicy.IsSuppressedResponseHeader("Transfer-Encoding"));
        Assert.IsTrue(WebAppResponsePolicy.IsSuppressedResponseHeader("Upgrade"));
        Assert.IsTrue(WebAppResponsePolicy.IsSuppressedResponseHeader("Keep-Alive"));
    }

    [TestMethod]
    public void DoesNotSuppressOrdinaryResponseHeaders()
    {
        Assert.IsFalse(WebAppResponsePolicy.IsSuppressedResponseHeader("Content-Type"));
        Assert.IsFalse(WebAppResponsePolicy.IsSuppressedResponseHeader("Content-Length"));
        Assert.IsFalse(WebAppResponsePolicy.IsSuppressedResponseHeader("Set-Cookie"));
        Assert.IsFalse(WebAppResponsePolicy.IsSuppressedResponseHeader("Content-Security-Policy"));
    }

    [TestMethod]
    public void RemovesTheFrameAncestorsDirectiveAndKeepsTheRest()
    {
        var rewritten = WebAppResponsePolicy.RewriteContentSecurityPolicy(
            "default-src 'self'; frame-ancestors 'none'; script-src 'self' https://cdn.example");

        Assert.AreEqual("default-src 'self'; script-src 'self' https://cdn.example", rewritten);
    }

    [TestMethod]
    public void MatchesFrameAncestorsCaseInsensitively()
    {
        var rewritten = WebAppResponsePolicy.RewriteContentSecurityPolicy(
            "default-src 'self'; Frame-Ancestors https://other.example");

        Assert.AreEqual("default-src 'self'", rewritten);
    }

    [TestMethod]
    public void ReturnsNullWhenOnlyFrameAncestorsRemains()
    {
        Assert.IsNull(WebAppResponsePolicy.RewriteContentSecurityPolicy("frame-ancestors 'none'"));
    }

    [TestMethod]
    public void KeepsAPolicyThatHasNoFrameAncestorsDirective()
    {
        const string policy = "default-src 'self'; img-src 'self' data:";

        Assert.AreEqual(policy, WebAppResponsePolicy.RewriteContentSecurityPolicy(policy));
    }

    [TestMethod]
    public void DoesNotRemoveADirectiveThatMerelyStartsWithFrame()
    {
        var rewritten = WebAppResponsePolicy.RewriteContentSecurityPolicy(
            "frame-src 'self'; frame-ancestors 'self'");

        Assert.AreEqual("frame-src 'self'", rewritten);
    }

    [TestMethod]
    public void ReturnsNullOrEmptyInputUnchanged()
    {
        Assert.IsNull(WebAppResponsePolicy.RewriteContentSecurityPolicy(null));
        Assert.AreEqual(string.Empty, WebAppResponsePolicy.RewriteContentSecurityPolicy(string.Empty));
    }

    [TestMethod]
    public void StripsFrameAncestorsFromEveryCommaSeparatedPolicy()
    {
        Assert.AreEqual(
            "default-src 'self'",
            WebAppResponsePolicy.RewriteContentSecurityPolicy("default-src 'self', frame-ancestors 'none'"));
        Assert.AreEqual(
            "default-src 'self'",
            WebAppResponsePolicy.RewriteContentSecurityPolicy("frame-ancestors 'self', default-src 'self'"));
    }

    [TestMethod]
    public void KeepsSurvivingPoliciesWhenOnlySomeAreEmptied()
    {
        Assert.AreEqual(
            "default-src 'self', script-src 'self'",
            WebAppResponsePolicy.RewriteContentSecurityPolicy(
                "frame-ancestors 'none', default-src 'self'; frame-ancestors 'self', script-src 'self'"));
    }

    [TestMethod]
    public void FilterForwardedCookiesRemovesJulOsCookiesAndKeepsTheRest()
    {
        Assert.AreEqual(
            "sid=abc; theme=dark",
            WebAppResponsePolicy.FilterForwardedCookies("sid=abc; .JulOS.Session=xyz; theme=dark; .JulOS.Antiforgery=q"));
    }

    [TestMethod]
    public void FilterForwardedCookiesMatchesTheJulOsPrefixCaseInsensitively()
    {
        Assert.AreEqual("a=1", WebAppResponsePolicy.FilterForwardedCookies(".julos.session=x; a=1"));
    }

    [TestMethod]
    public void FilterForwardedCookiesReturnsNullWhenNothingRemains()
    {
        Assert.IsNull(WebAppResponsePolicy.FilterForwardedCookies(".JulOS.Session=xyz"));
        Assert.IsNull(WebAppResponsePolicy.FilterForwardedCookies(null));
        Assert.IsNull(WebAppResponsePolicy.FilterForwardedCookies("   "));
    }

    [TestMethod]
    public void FilterForwardedCookiesKeepsAllCookiesWhenNoneAreJulOs()
    {
        Assert.AreEqual("a=1; b=2", WebAppResponsePolicy.FilterForwardedCookies("a=1; b=2"));
    }
}
