using JulOS.Domain.Sessions;

namespace JulOS.Domain.Tests.Sessions;

/// <summary>Verifies the protocol-neutral request a session reference is created from.</summary>
[TestClass]
public sealed class SessionRequestTests
{
    [TestMethod]
    public void AKindAndATargetReferenceAreAccepted()
    {
        var request = new SessionRequest("de.juloc.julos.browser.session", "target-1");

        Assert.AreEqual("de.juloc.julos.browser.session", request.Kind);
        Assert.AreEqual("target-1", request.TargetReference);
    }

    [TestMethod]
    public void AnEmptyKindIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SessionRequest(string.Empty, "target-1"));
    }

    [TestMethod]
    public void AWhitespaceTargetReferenceIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SessionRequest("kind", "   "));
    }

    [TestMethod]
    public void TwoRequestsWithTheSameValuesAreEqual()
    {
        Assert.AreEqual(
            new SessionRequest("kind", "target-1"),
            new SessionRequest("kind", "target-1"));
    }
}
