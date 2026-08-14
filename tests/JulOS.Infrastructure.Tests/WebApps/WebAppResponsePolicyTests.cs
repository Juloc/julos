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
}
